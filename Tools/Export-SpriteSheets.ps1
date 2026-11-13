param([string]$ImageDirectory = (Join-Path $PSScriptRoot '..\SmileyImagens'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore
$ImageDirectory = [IO.Path]::GetFullPath($ImageDirectory)
$sheets = @(
    @{ Name = 'Folha de sprites_ corrida do personagem encapuzado.png'; Folder = 'Corrida' },
    @{ Name = 'Folhagens do Caminho em Oito Passos.png'; Folder = 'Caminhada' }
)
foreach ($sheet in $sheets) {
    $sourcePath = Join-Path $ImageDirectory $sheet.Name
    $stream = [IO.File]::OpenRead($sourcePath)
    try {
        $source = [Windows.Media.Imaging.BitmapFrame]::Create($stream, [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat, [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    } finally { $stream.Dispose() }
    if ($source.PixelWidth -ne 1774 -or $source.PixelHeight -ne 887 -or $source.Format.ToString() -ne 'Bgra32') {
        throw "A folha deve medir 1774x887 e usar BGRA32: $sourcePath"
    }
    $cells = @()
    for ($row = 0; $row -lt 2; $row++) {
        for ($column = 0; $column -lt 4; $column++) {
            # Shared proportional boundaries: every source pixel belongs to exactly one cell.
            $x0 = [int][Math]::Round($column * 1774.0 / 4, [MidpointRounding]::AwayFromZero)
            $x1 = [int][Math]::Round(($column + 1) * 1774.0 / 4, [MidpointRounding]::AwayFromZero)
            $y0 = [int][Math]::Round($row * 887.0 / 2, [MidpointRounding]::AwayFromZero)
            $y1 = [int][Math]::Round(($row + 1) * 887.0 / 2, [MidpointRounding]::AwayFromZero)
            $width = $x1 - $x0; $height = $y1 - $y0
            $crop = [Windows.Media.Imaging.CroppedBitmap]::new($source, [Windows.Int32Rect]::new($x0, $y0, $width, $height))
            $pixels = [byte[]]::new($width * $height * 4)
            $crop.CopyPixels($pixels, $width * 4, 0)
            $left = $width; $top = $height; $right = -1; $bottom = -1; $transparent = 0
            for ($y = 0; $y -lt $height; $y++) {
                for ($x = 0; $x -lt $width; $x++) {
                    if ($pixels[($y * $width + $x) * 4 + 3] -eq 0) { $transparent++; continue }
                    $left = [Math]::Min($left, $x); $right = [Math]::Max($right, $x)
                    $top = [Math]::Min($top, $y); $bottom = [Math]::Max($bottom, $y)
                }
            }
            if ($transparent -eq 0 -or $right -lt $left) { throw "Quadro sem transparência ou vazio: $($sheet.Name) $row/$column" }
            $cells += @{ Pixels = $pixels; Width = $width; Height = $height; Left = $left; Top = $top; Right = $right; Bottom = $bottom; SourceX = $x0; SourceY = $y0 }
        }
    }
    $baseline = 443 # Shared ground reference of the 444px canvas, including airborne poses.
    $folder = Join-Path $ImageDirectory $sheet.Folder
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $metadata = @()
    for ($i = 0; $i -lt $cells.Count; $i++) {
        $cell = $cells[$i]
        # Keep a COMMON grid origin. Recentring each silhouette moves the hood/body
        # when arms or feet change pose; grounding airborne feet would introduce bobbing.
        # Pad the 443px cells to 444px without scaling, preserving their ground reference.
        $dx = [int][Math]::Floor((444 - $cell.Width) / 2.0)
        $dy = [int][Math]::Floor((444 - $cell.Height) / 2.0)
        if ($cell.Left + $dx -lt 0 -or $cell.Right + $dx -ge 444 -or $cell.Top + $dy -lt 0 -or $cell.Bottom + $dy -ge 444) {
            throw 'O alinhamento cortaria pixels visíveis.'
        }
        $output = [byte[]]::new(444 * 444 * 4)
        for ($y = 0; $y -lt $cell.Height; $y++) {
            $targetY = $y + $dy
            if ($targetY -lt 0 -or $targetY -ge 444) { continue }
            $sourceX = [Math]::Max(0, -$dx)
            $targetX = [Math]::Max(0, $dx)
            $length = [Math]::Min($cell.Width - $sourceX, 444 - $targetX)
            if ($length -gt 0) { [Array]::Copy($cell.Pixels, ($y * $cell.Width + $sourceX) * 4, $output, ($targetY * 444 + $targetX) * 4, $length * 4) }
        }
        $bitmap = [Windows.Media.Imaging.BitmapSource]::Create(444, 444, 96, 96, [Windows.Media.PixelFormats]::Bgra32, $null, $output, 444 * 4)
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $file = 'frame_{0:D2}.png' -f ($i + 1)
        $outputPath = Join-Path $folder $file
        $outputStream = [IO.File]::Create($outputPath)
        try { $encoder.Save($outputStream) } finally { $outputStream.Dispose() }
        $metadata += [ordered]@{ File = $file; X = $cell.SourceX; Y = $cell.SourceY; Width = $cell.Width; Height = $cell.Height; OffsetX = $dx; OffsetY = $dy; Top = $cell.Top + $dy; Baseline = $baseline }
    }
    [ordered]@{ Source = $sheet.Name; SourceSha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash; FrameSize = 444; Frames = $metadata } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $folder 'frames.json') -Encoding utf8
    Write-Output "$($sheet.Folder): 8 quadros de 444x444; chão=$baseline; alfa e pixels preservados."
}
