# Smiley Desktop — Mark IV.B: Ollama local e seleção de modelos

Mascote C#/WPF para Windows, .NET 10, sem pacotes externos. A conversa permite escolher Demonstração ou Ollama local, com contexto curto e seleção de modelo instalado. Texto flutuante, física, autonomia, navegação e acompanhamento do apoio a até 60 Hz continuam existentes. A exploração de janelas começa **desativada**. Nenhum PNG foi alterado.

## Executar

```powershell
dotnet restore .\Smiley.Desktop.csproj
dotnet build .\Smiley.Desktop.csproj --no-restore
dotnet run --project .\Smiley.Desktop.csproj --no-build
```

Clique direito: **Conversar com Smiley**, **Pausar/Retomar**, **Autonomia ativada/desativada**, **Explorar janelas: ativado/desativado**, **Pular**, Modo de deslocamento → **Automático/Caminhar/Correr**, sempre visível e Sair. O modo inicial é Automático, com autonomia ligada e exploração de janelas desligada. Clique esquerdo provoca reação. Segurar e mover permite arrastar; um arrasto rápido arremessa o personagem ao soltar, e um arrasto lento inicia uma queda simples. Os argumentos opcionais `--inspect --paused` expõem a janela na barra de tarefas para ferramentas de teste e iniciam pausado.

## Mark IV.B — Ollama e modelos locais

Abra a conversa com **Alt+S** e escolha **Provedor → Ollama local**. A lista de modelos é consultada em `http://localhost:11434/api/tags`; use **Atualizar** para consultá-la novamente. Escolha explicitamente um modelo instalado. A troca não reinicia o Smiley nem apaga as mensagens visíveis. A escolha anterior pode ser restaurada; não é escolhido silenciosamente o primeiro modelo da lista. O indicador informa disponibilidade, ausência de modelos ou falha. **Demonstração local** continua disponível, identificada como simulação, e nunca substitui automaticamente uma falha do Ollama.

O Smiley não instala, inicia ou baixa nada. Para usar respostas reais, instale/abra o Ollama manualmente e disponibilize um modelo local por ele. Se necessário, inicie o servidor com `ollama serve`; para instalar um modelo de sua escolha, execute `ollama pull NOME_DO_MODELO` (substitua o nome). Depois clique em Atualizar. O endereço da interface é exclusivamente local; não há configuração de serviços de nuvem, credenciais ou autenticação. Modelos sem pesos locais, com `remote_host`/`remote_model` ou tags cloud são omitidos; a condição é revalidada antes de enviar uma conversa. A política do próprio servidor Ollama permanece sob controle do usuário.

`OllamaChatProvider` usa `HttpClient` compartilhado e `System.Text.Json`, seguindo a API oficial de [modelos](https://docs.ollama.com/api/tags) e [conversa](https://docs.ollama.com/api/chat). Envia `stream:false`, modelo selecionado, instrução de sistema e até oito mensagens recentes, apenas texto. A identidade provisória em `SmileyIdentity.cs` é independente do modelo: português brasileiro, curiosidade, sarcasmo bem-humorado, respostas curtas e honestidade sobre não ver a tela/não executar ações. Não há ferramentas ou execução de comandos a partir da resposta. O histórico mantém a resposta completa; somente a fala flutuante aplica o limite visual existente. Mensagens da demonstração são rotuladas também no contexto, para não passarem por respostas reais do modelo.

As operações são assíncronas, com espera de até 5 s para listar modelos e 3 min para uma resposta, além da consulta inicial de modelos. O carregamento inicial pode demorar. **Cancelar**, Esc, fechamento do aplicativo ou troca de provedor/modelo interrompem solicitações. Um identificador de versão impede publicar respostas antigas depois dessas mudanças. Não há HTTP no ciclo de renderização, streaming visual ou controle de movimentos por IA. O limite de geração é 512 tokens; respostas vazias/inválidas e falhas são mostradas claramente, sem fallback silencioso.

Somente provedor e nome do último modelo são salvos em `%LOCALAPPDATA%\Smiley\chat-settings.json`. O histórico de até oito entradas fica em memória e é descartado ao fechar a conversa. Nenhuma captura de tela, título de janela, arquivo ou dado de outros aplicativos é incluído nos pedidos.

**Verificação:** build do projeto sem avisos/erros. A suíte histórica não foi executada nem modificada. Uma checagem pontual com fixture HTTP temporária, sem modelo/IA, confirmou listagem de dois modelos locais, exclusão de um remoto antes do POST, modelo escolhido, contexto/sistema, `stream:false`, resposta completa maior que 500 caracteres, cancelamento e JSON inválido. No Windows foram observadas a demonstração respondendo, a seleção de Ollama e, quando o servidor ainda não estava disponível, seu aviso de indisponibilidade, com Enviar bloqueado sem modelo e sem fallback simulado; o processo permaneceu responsivo.

**Validação real posterior:** depois que o usuário instalou o Ollama e solicitou explicitamente `ollama pull qwen3:4b-instruct`, o modelo foi baixado e verificado com sucesso (aproximadamente 2,5 GB). O `OllamaChatProvider` real, com a identidade do Smiley, recebeu uma resposta em português; a segunda pergunta recuperou corretamente a palavra "abacaxi" do contexto anterior. O primeiro carregamento/resposta levou 50,5 s neste teste. Provedor Ollama e modelo `qwen3:4b-instruct` foram salvos nas preferências e o mascote foi reaberto. A inferência real foi verificada pelo componente; a seleção visual desse modelo instalado não pôde ser reinspecionada porque a ferramenta de controle da interface deixou de responder. A instalação/download ocorreu por esse pedido expresso do usuário, sem adicionar download automático ao aplicativo.

Arquivos novos: `OllamaChatProvider.cs`, `ChatSettings.cs`, `SmileyIdentity.cs`. Alterados: `IChatProvider.cs`, `SmileyChatWindow.xaml`/`.xaml.cs`, `MainWindow.xaml`/`.xaml.cs` e `README.md`. Overlay, física, comportamento, navegação, tracker, sprites e PNGs permanecem intactos nesta etapa. Limites: somente loopback local, sem streaming; alguns modelos podem não responder bem dentro do limite de geração. A execução real depende do Ollama e dos recursos da máquina. Não foi iniciada a Mark IV.C.

## Mark IV.A — conversa local de demonstração (histórico)

Refinamento visual: o texto agora fica preferencialmente **acima e deslocado para um dos lados**, ligado ao topo do capuz por uma **diagonal curta**, sem atravessar o rosto. `OutlinedSpeechText.cs` desenha letras brancas com contorno preto e sombra reforçada para melhorar o contraste em fundos claros. Os contornos são armazenados e reconstruídos apenas quando texto, medida ou DPI mudam. Perto do topo da tela, permanecem as alternativas laterais/abaixo. A aparência foi conferida no Windows; compilação sem avisos/erros, sem repetir a suíte histórica. Os estilos finais transparentes e sem foco continuam preservados.

**Alt+S** abre a janela escura de conversa e foca a entrada; repetir o atalho traz a mesma janela à frente, inclusive se estiver minimizada. **Enter** envia, **Shift+Enter** quebra a linha e **Esc** fecha somente a conversa. O menu **Conversar com Smiley** oferece o mesmo acesso. O registro usa [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey), apenas Alt+S, com NOREPEAT, e é liberado no fechamento do mascote. Se o Windows recusar o atalho por conflito, o menu continua disponível e seu tooltip informa a indisponibilidade. Não há hooks de teclado nem leitura de digitação fora desta janela.

`IChatProvider.GetReplyAsync` prepara a substituição do provedor na etapa seguinte. `MockChatProvider` usa frases predefinidas e uma espera assíncrona curta; a interface identifica claramente a demonstração. Nenhuma chamada HTTP, Ollama, modelo, autenticação, análise de aplicativos, comentário espontâneo ou observação de inatividade foi acrescentada. A conversa aceita até 1.000 caracteres por envio e mantém apenas oito entradas recentes em memória, descartadas ao fechar. Um envio fica em andamento por vez; texto digitado para o próximo envio é conservado. Fechar cancela a resposta pendente.

A resposta aparece em `SmileySpeechOverlay`: **somente texto branco de 17 DIPs com sombra discreta e uma linha de 1,4 DIP apontando para o capuz**, sem painel, balão, borda ou ícone. O fundo da janela é transparente, sem foco/barra de tarefas; a combinação layered/WS_EX_TRANSPARENT faz os cliques passarem para os aplicativos atrás dela, conforme a [documentação de janelas layered](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features). Esse comportamento não é aplicado à janela de digitação. A largura padrão é 320 DIPs, configurável por `MaximumTextWidth`; o texto visível é limitado a 360 elementos de texto e a duração vai de 5 a 18 s. Uma nova resposta substitui a anterior. O componente recebe strings independentemente do provedor, podendo futuramente receber trechos ou comentários por esse mesmo caminho.

O overlay reutiliza `MainWindow.Place` e o ciclo visual existente: segue caminhada, salto, arrasto e apoio sem um novo timer. A referência é a cabeça, não os pés. As posições Win32 são físicas; o texto é medido em DIPs no monitor de destino, incluindo coordenadas negativas. Prefere a esquerda perto da borda direita e usa alternativas acima/ao lado/abaixo com limitação à área útil. Oculto, não mede/reorganiza conteúdo nem consulta geometria para a fala. Abrir/enviar mensagens não solicita movimento, pausa ou ação comportamental. Janelas, hooks e cancelamento são encerrados junto do aplicativo.

**Verificação desta etapa:** somente `dotnet build` e inspeção funcional pontual, conforme solicitado; zero avisos/erros na compilação final. A suíte histórica de 603 verificações não foi repetida e o sistema de testes e `test-results.txt` não foram alterados. No Windows, Alt+S foi acionado a partir do Explorador, a entrada recebeu foco, Enter enviou/limpou o campo e a resposta apareceu no histórico. Também foi observado o texto flutuante à esquerda, em duas linhas, com linha fina apontando para o capuz; Esc fechou a conversa mantendo o mascote aberto. Para capturar a aparência do overlay pela ferramenta, ele foi exposto temporariamente à inspeção; a versão final restaura todos os estilos sem foco/sem barra de tarefas.

Limites: respostas ensaiadas, sem IA real; não há streaming ou persistência. Passagem efetiva de cliques para diferentes aplicativos, fundos claros e hardware de múltiplos monitores com DPI mistos ainda pedem conferência visual mais ampla. A fala longa é truncada e um personagem temporariamente fora da área útil terá a ponta da linha limitada à região visível. Não foi iniciada a Mark IV.B.

Arquivos criados: `IChatProvider.cs` (inclui `MockChatProvider`), `SmileyChatHotKey.cs`, `SmileyChatWindow.xaml` e `.xaml.cs`, `SmileySpeechOverlay.xaml` e `.xaml.cs`. Alterados: `MainWindow.xaml`, `MainWindow.xaml.cs` e `README.md`. Controladores de física/comportamento/navegação, tracker, provider de plataformas, sprites, PNGs, projetos e dependências permanecem preservados.

## Mark III.C — exploração e saltos direcionados (histórico)

Para testar, deixe a autonomia ligada, ative **Explorar janelas** e mantenha janelas próximas, com bordas em alturas alcançáveis. É possível arrastar o Smiley sobre uma janela para iniciar a exploração dali. Do chão, apenas bordas a menos de aproximadamente 96 DIPs de altura são fisicamente alcançáveis; o planejador ainda reserva margem vertical. Não espere que ele alcance imediatamente uma janela alta ou distante. As preferências e a exploração não são persistidas: reiniciar volta à exploração desligada.

### Planejamento e execução

`SmileyNavigationPlanner` é uma classe sem timer, APIs nativas ou execução de movimento. Avalia um salto por vez, sem grafo de rotas, escalada ou pathfinding. Recebe posição física, apoio atual/chão, limites efetivos do controlador, mapa e saúde da coleta. Prioriza a menor soma de distância até a partida com distância até o destino. Um segmento diferente da mesma janela também pode ser destino. Não escolhe janelas ocultas, insuficientes, fora do monitor atual ou fisicamente inalcançáveis.

Na cadência existente de **100 ms**, o `SmileyBehaviorController` pode escolher uma intenção Explore ao terminar um descanso: chance padrão de 30%, somente com mapa saudável e cooldown disponível. Não há sorteios por frame ou segundo loop de autonomia. Os comportamentos Idle, Wander, Run, Hop, Curious e Flee continuam presentes. Com a exploração desligada, as decisões anteriores e suas colisões permanecem disponíveis.

O fluxo é: escolher destino → caminhar até uma partida segura → revalidar destino e trajetória → solicitar salto → aguardar aterrissagem real → descansar. A aproximação usa caminhada de duração finita, calculada por distância/velocidade, sem teleporte. Na exploração local, Wander/Run escolhem um ponto aleatório seguro na superfície atual e param ao alcançá-lo. As bordas reservam toda a faixa de pés (24 DIPs) e mais 8 DIPs para cada lado: são necessários 40 DIPs para um ponto de apoio planejado seguro. Isso não muda as condições de colisão manual da Mark III.B.

O plano é reavaliado quando muda o segmento/origem, a geometria relevante do destino, DPI/área útil, ou quando chega a hora de saltar. Uma mudança de snapshot sem relação com o plano não o recalcula. O movimento rápido da origem continua sendo observado a até 60 Hz e reconciliado pela física; o planejador só avalia sua nova posição a cada decisão de 100 ms. As outras janelas e a ordem Z continuam na consulta geral de 200 ms.

Se o destino some, é minimizado, fica coberto ou pequeno demais antes da partida, a aproximação para. Uma falha de coleta impede planejamento imediatamente, mesmo durante a tolerância de um segundo do apoio físico. Durante o voo não há correção de trajetória nem garantia de chegada: o mapa atualizado e a colisão contínua decidem a superfície real. Aterrissar em outro lugar conta como falha. Há cooldown global de 8 s após uma tentativa e cooldown de 30 s por HWND após falha, com armazenamento temporário limitado e sem persistência. Depois de aterrissar também se aplica o descanso normal de 6–16 s.

### Matemática e limites efetivos

O controlador expõe seus próprios `MotionLimits`; o planejador não duplica constantes configuráveis. Os padrões são gravidade `g=1100` DIPs/s², impulso vertical `J=460` DIPs/s, caminhada 65 e corrida 140 DIPs/s. Na fronteira com a física, `g` e `J` usam a escala Y; velocidades horizontais e margens de apoio usam a escala X. Retângulos DWM, posição, altura e velocidades de voo são pixels físicos, inclusive com coordenadas negativas.

Com Y crescendo para baixo, a altura dos pés é `Y(t) = Y0 - J*t + g*t²/2`. Para uma superfície em `Ydest`, com `dy=Ydest-Y0`, o instante descendente é:

```text
t = (J + sqrt(J² + 2*g*dy)) / g
vx = (Xchegada - Xpartida) / t
```

O salto só é aceito se o discriminante permite descida, há espaço para o arco, o destino comporta a faixa segura dos pés, e `|vx|` cabe na velocidade disponível. Em Caminhar, o limite é 65 DIPs/s; em Automático/Correr, até 140 DIPs/s. Também são verificadas outras bordas que poderiam interceptar a descida antes do destino. As posições de partida/chegada ficam dentro da área útil do monitor, sem navegação deliberada entre monitores.

O ápice físico é **96,18 DIPs**; o planejador reserva 8 DIPs, admitindo subida de até aproximadamente **88,18 DIPs**, além de exigir 12 DIPs de folga acima do corpo no ápice. Na mesma altura, o voo dura **0,836 s**, permitindo no máximo **54,36 DIPs** horizontais com velocidade de caminhada ou **117,09 DIPs** com corrida. Essas distâncias são entre referências horizontais do personagem; a lacuna útil entre bordas é menor por causa das margens. Destinos mais altos reduzem o tempo disponível; destinos inferiores podem aumentá-lo. Não há alcance horizontal universal independente da altura.

`TryDirectedJump(vx, scaleX, scaleY)` apenas solicita o mesmo salto existente com componente horizontal limitada à corrida. `TryJump` manual continua vertical. Integração balística, gravidade, arremesso, máscara alfa, animações e colisões contínuas são reutilizados. Não há salto duplo, aceleração especial ou reposicionamento artificial para completar o plano.

### Prioridades, controle e diagnóstico

Pausa congela plano, movimento, descansos e cooldowns. Arrasto, clique e arremesso cancelam a intenção; o intervalo de tranquilidade de 2 s após interação manual permanece. Curious/Flee podem interromper a aproximação no chão; uma fuga não é disparada durante um salto planejado já iniciado. Desligar exploração/autonomia durante o voo cancela apenas a intenção, preservando velocidades e a aterrissagem física. Pular, arrastar e arremessar continuam funcionando com exploração desligada.

O diagnóstico existente, opcional e desligado por padrão, acrescenta estado/rejeição simplificada da navegação, HWND de origem e destino, X planejado de partida/chegada e Y previsto dos pés. X é a posição da janela do mascote, não a borda do PNG. O apoio atual continua destacado no mapa. Não se abriu outra janela de configurações/diagnóstico; não são lidos títulos, textos ou conteúdo dos aplicativos.

### Validação e limitações da Mark III.C

**603 verificações aprovadas**: todas as 552 da base atual e 51 de navegação. Incluem margens e exploração local repetida, escolha próxima, trajetórias/limites configuráveis, preferência Caminhar, rejeição por altura/distância/headroom/oclusão, segmentos do mesmo HWND, origem no chão, aproximação, aterrissagem efetiva, alvo dinâmico, perda de origem, pausas, interações, cursor, toggles e aplicação/expiração de cooldowns. Há testes em coordenadas negativas e escalas 100/125/150/200%, determinismo com semente fixa e equivalência entre atualizações de 370 ms e renderização a 144 Hz. Os testes existentes de física, comportamento com exploração desligada, sprites, PNGs, DPI, janelas e acompanhamento rápido continuam passando. O teste histórico de hashes preserva sprites e estimador de arremesso; comportamento e controlador físico foram alterados intencionalmente e são cobertos pelas regressões.

A integração usa duas janelas WPF reais, lê seus retângulos pelo DWM e confirma que o controlador realmente aterrissa no HWND de destino, com os pés na borda medida. A janela WPF principal também verifica a opção desligada inicialmente e os handlers reais de ativar/desativar. Na inspeção visual do Windows foram observados o mascote, a nova opção e seu estado padrão desativado. A travessia entre janelas foi comprovada por integração automatizada; não foi observada uma sessão visual longa de navegação espontânea. Restore/build sem avisos ou erros; log completo em `test-results.txt`.

Limites: planejamento conservador de um salto, sem procura de sequência de plataformas; não há previsão de movimento do alvo ou controle no ar. Geometria retangular e consulta do alvo a aproximadamente 200 ms podem tornar uma previsão inválida durante o voo. A exploração planejada protege as margens, mas interações manuais, fugas e remoção dinâmica da superfície continuam podendo causar queda. A passagem por hardware real com vários DPI segue pendente; cenários de coordenadas/escalas são cobertos sinteticamente. Não foram medidos 60 FPS constantes. Não foi iniciada a Mark IV.

Arquivos novos nesta etapa: `SmileyNavigationPlanner.cs`, `Tests/NavigationChecks.cs`. Alterados: `SmileyBehaviorController.cs`, `SmileyController.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `WindowTrackerDiagnosticWindow.cs`, `Tests/Program.cs`, `README.md` e `test-results.txt`. Tracker nativo, provider de plataformas, acompanhamento rápido, estimador de arremesso, sprites e todos os PNGs permanecem sem alteração nesta etapa. Nenhuma dependência externa acrescentada.

## Mark III.B — superfícies e apoio físico (histórico)

`WindowPlatformProvider` converte cada borda superior em segmentos horizontais em **pixels físicos**. Apenas retângulos visíveis com ordem Z superior e que realmente intersectam a altura da borda bloqueiam a superfície. Os intervalos bloqueados são ordenados e unidos, produzindo lacunas determinísticas, sem duplicações. Uma borda de X=100..600 coberta em 300..450 oferece 100..300 e 450..600. Segmentos menores que 24 DIPs, convertidos pelo DPI atual, são descartados.

O mapa imutável mantém HWND, índice da geometria original, versão do snapshot, extremos, altura e disponibilidade. Não replica um retângulo em cada fragmento. Os buffers de cálculo são reutilizados; o mapa geral só é reconstruído quando muda o snapshot ou o mínimo de apoio por DPI. A geometria completa continua sendo consultada pelo tracker a cada 200 ms, independentemente da renderização de até 60 Hz e das decisões a cada 100 ms.

**Acompanhamento fluido do apoio:** enquanto existe suporte ativo e a simulação não está pausada, arrastada ou com menu aberto, a vista consulta somente esse HWND a cada atualização visual, até 60 Hz. `WindowTracker.TryRefreshWindow` reutiliza a leitura nativa de identidade/visibilidade/DWM e valida processo/thread, sem `EnumWindows`, varredura de ordem Z ou alteração do snapshot geral. `WindowPlatformProvider.RefreshSupport` recalcula apenas os segmentos dessa janela contra as outras geometrias já conhecidas. O restante das plataformas, ordem Z e diagnóstico geral permanece na cadência de 200 ms. Uma sobreposição imutável é reutilizada quando a geometria não muda; a física recebe o mapa atualizado pelo mesmo caminho de reconciliação já existente. `SmileyController.cs` não foi modificado por esse ajuste: gravidade, colisões, impulsos e saltos são os mesmos. Não há interpolação, timer extra ou consultas quando o personagem está no chão/voando sem apoio. A saúde do mapa geral continua obrigatória; um HWND inválido, reciclado ou minimizado não mantém apoio fictício.

### Colisão e referência dos pés

As plataformas são de sentido único: o personagem atravessa enquanto sobe e aterrissa na primeira superfície válida cruzada durante a descida. O `SmileyController` resolve o instante de cruzamento na trajetória de aceleração constante que já utilizava e verifica o contato horizontal **nesse instante**, incluindo arremessos rápidos. O intervalo restante continua sendo integrado após aterrissagens, paredes e saídas de apoio; não se testa apenas a posição final do frame. O chão da área útil permanece disponível e há apenas um apoio ativo.

O alinhamento usa a mesma referência visual `GroundLine` e transformação uniforme dos sprites existentes. A região de apoio fica centrada nos pés, com largura de 24 DIPs e contato mínimo de 8 DIPs, convertidos em pixels físicos. Braços, roupa e limite inferior do arquivo não definem a colisão. Aterrissar alinha os pés e zera a velocidade vertical; caminhar/correr e seus oito quadros continuam funcionando. O impulso horizontal de arremesso é absorvido na aterrissagem, como já ocorria no chão; deslocamento de caminhada/corrida pode continuar no passo seguinte.

Ao iniciar arrasto ou saltar, o vínculo é removido. A soltura manual permite contato com tolerância vertical de 2 DIPs; fora disso, somente a travessia descendente captura uma superfície. Uma janela recém-aparecida na altura dos pés ou que atravessa o corpo não o prende arbitrariamente. Ao ultrapassar o contato mínimo de uma borda, o personagem cai pela mesma gravidade e pose existentes, conservando a velocidade horizontal da saída. Não há prevenção autônoma de quedas nem escolha de destinos.

### Acompanhamento e perda de apoio

`WindowSupport` guarda HWND, segmento, geometria de origem e X relativo à janela. Novos snapshots reposicionam o Smiley pela nova origem mais esse deslocamento relativo, já atualizado durante a caminhada. O segmento e o contato são revalidados antes de mover o personagem. O monitor de destino é escolhido em coordenadas físicas; não se misturam DIPs com retângulos DWM. Se o vínculo deixa de ser válido por fechamento, minimização, ocultação, redimensionamento ou oclusão do apoio, ele é removido sem teleporte para outra superfície, e a queda parte da posição atual.

Durante pausa global, posição, velocidades e apoio ficam congelados. O tracker continua observando e o apoio é reconciliado na retomada, sem acumular tempo de física. Desativar apenas a autonomia mantém a física ativa.

Falhas temporárias de coleta preservam o último mapa durante **até 1 segundo** desde o último heartbeat bem-sucedido observado. Após isso, plataformas ficam indisponíveis e um apoio é liberado. O prazo usa o Stopwatch monotônico existente; uma flag antiga de sucesso não renova o prazo indefinidamente. Coletas saudáveis sem mudança de geometria renovam a saúde sem recriar o mapa. A recuperação permite novas aterrissagens, sem puxar um personagem em queda de volta para cima.

### Diagnóstico, testes e limites atuais

O diagnóstico da Mark III.A continua único, opcional e desligado por padrão. Mostra segmentos verdes, apoio ativo em laranja e bordas em cinza, além de intervalos físicos e aviso de mapa indisponível. Use **Diagnóstico de janelas** no menu ou `--window-diagnostics`. Não lê títulos/conteúdo de aplicativos nem grava histórico.

Restore e build passaram sem avisos ou erros. **552 verificações aprovadas**: as 509 anteriores, 35 cenários de plataformas e 8 verificações do acompanhamento rápido. Cobrem geometria, oclusões múltiplas, aterrissagem, subida, arremesso diagonal rápido, contato dos pés, caminhada/corrida, saída de borda, salto, acompanhamento, redimensionamento, minimização/fechamento/ocultação, HWND reciclado, pausa, saúde e recuperação. Incluem coordenadas negativas, escalas de 100/125/150/200%, comparação de passos longos com 60 Hz e integração DWM com janelas WPF reais. O ajuste rápido valida 60 amostras de um único HWND entre consultas gerais, imutabilidade/reuso, ausência de retorno à posição antiga, invalidação e leitura nativa de movimento antes do próximo poll. Isso testa a cadência lógica; não constitui medição de 60 FPS constantes na tela. Os hashes continuam conferindo todos os PNGs e os três arquivos preservados; o controlador físico é coberto pelos testes anteriores e novos. Registro completo em `test-results.txt`.

Validação visual no Windows: o personagem foi arrastado enquanto pausado, retomou a simulação e pousou com Y=650 e pés em Y=840 sobre a janela WPF de teste. Movê-la de X=1457 para X=1377 deslocou o Smiley de X=1650 para X=1570, mantendo a altura. Minimizá-la fez o personagem cair até o chão, Y=842/pés=1032. O diagnóstico também mostrou o segmento exposto da janela. Fechamento e os demais cenários foram validados por testes automatizados; não se confunde isso com interação visual manual. Para repetir com uma janela descartável: `dotnet run --project .\Tests\Smiley.Tests.csproj -- --platform-fixture` (esse modo somente abre a janela de teste; omita a opção para executar a suíte).

Limites conhecidos nesta versão:

- A janela de apoio é lida até 60 vezes por segundo, conforme a renderização efetiva do WPF. As outras janelas e mudanças de ordem Z continuam com a latência do mapa geral de aproximadamente 200 ms; não há interpolação.
- Usa retângulos, sem transparência interna, cantos arredondados ou conteúdo de aplicativos. A classificação de janelas continua a heurística documentada abaixo.
- Bordas sem espaço para o corpo acima delas dentro da área útil do monitor não recebem apoio físico. Isso inclui normalmente a borda superior de janelas maximizadas: o segmento geométrico existe, mas não estaciona o Smiley fora da tela.
- Monitores com coordenadas negativas e DPI diferentes têm cobertura sintética; a passagem entre monitores com escalas diferentes permanece pendente de validação em hardware real.
- Janelas podem mudar entre consultas; a próxima coleta reconcilia o estado. Não há navegação, planejamento de saltos ou proteção contra caminhar para fora da borda.

Arquivos novos: `WindowPlatformProvider.cs`, `Tests/PlatformChecks.cs`, `Tests/PlatformFixture.cs`. Alterados: `SmileyController.cs`, `NativeMethods.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `WindowTrackerDiagnosticWindow.cs`, `Tests/Program.cs`, `README.md` e `test-results.txt`. `WindowTracker.cs`, `NativeWindowGeometrySource.cs`, `SmileyBehaviorController.cs`, `SpriteManager.cs`, `DragVelocityTracker.cs` e PNGs permanecem preservados. Nenhuma dependência externa foi acrescentada.

O ajuste posterior do acompanhamento rápido acrescentou `Tests/SupportTrackingChecks.cs` e alterou `WindowTracker.cs`, `NativeWindowGeometrySource.cs`, `WindowPlatformProvider.cs`, `MainWindow.xaml.cs`, `Tests/Program.cs`, este README e o registro de testes. Física e comportamento permaneceram byte a byte iguais à entrega inicial da Mark III.B.

## Mark III.A — infraestrutura de observação geométrica

`WindowTracker` é iniciado no carregamento da MainWindow e descartado no fechamento. Um **DispatcherTimer próprio, a cada 200 ms**, agenda consultas em prioridade Background na mesma thread da interface. Ele é independente de `CompositionTarget.Rendering` (até 60 Hz) e dos 100 ms do `SmileyBehaviorController`. Não foi criada uma thread. O serviço continua observando quando a autonomia ou a física estão pausadas, sem movimentar o personagem ou influenciar decisões.

O intervalo é configurável no construtor: `new WindowTracker(hwnd, TimeSpan.FromMilliseconds(200))`; recomenda-se 150–250 ms. A API permite 50 ms a 10 s, rejeitando intervalos inválidos. Por ser um timer do dispatcher, o período é uma meta e pode sofrer atraso quando a interface estiver ocupada. A geometria nunca é enumerada a cada frame.

### Enumeração, classificação e ordem Z

1. `EnumWindows` descobre os HWNDs de nível superior do desktop atual.
2. Metadados Win32 filtram janelas inválidas, filhas, owned, tool windows, no-activate e transparent overlays. Classes técnicas do shell, barras de tarefas, menus e tooltips também são excluídas. A própria janela do Smiley e todas as janelas de seu processo, inclusive o diagnóstico, são ignoradas.
3. `IsIconic` identifica minimização; `IsWindowVisible` filtra ocultação; `DWMWA_CLOAKED` filtra janelas cloaked. `IsZoomed` fornece o estado maximizado.
4. O retângulo vem exclusivamente de `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`. Não há fallback para GetWindowRect, que poderia incluir bordas invisíveis ou coordenadas virtualizadas por DPI. Se o DWM não fornecer geometria válida, a janela fica indisponível para o mapa.
5. Uma passagem limitada de `GetTopWindow`/`GetWindow(GW_HWNDNEXT)` estabelece a ordem Z explicitamente. Usa detecção de ciclos e limite de 16.384 handles, evitando um loop nativo ilimitado. Se a enumeração e a ordem Z não puderem ser conciliadas, a coleta inteira é descartada e repetida no próximo período.
6. Antes de publicar, os handles/identidades são revalidados. Uma janela fechada depois da consulta DWM é removida. Janelas recém-minimizadas, ocultas ou cloaked também deixam o mapa ativo.

Não se exige uma dimensão arbitrária mínima para aceitar uma janela: basta um retângulo válido e a classificação de janela principal. Janelas layered comuns continuam permitidas quando não apresentam os estilos de overlay excluídos. Os nomes de classes são metadados técnicos para classificação, não títulos de documentos. Um buffer StringBuilder é reutilizado e a string de classe só é recriada quando muda, incluindo reutilização de handles.

### Snapshots, mudanças e futuras plataformas

`WindowTracker.Current` retorna um `WindowMapSnapshot` imutável, com publicação atômica por referência. `MainWindow.ObservedWindows` expõe esse mapa; na Mark III.B o provider o consome para fornecer superfícies à física. A camada de comportamento continua independente.

- `Version` e `CapturedAtUtc`: versão e instante da coleta que produziu a última mudança.
- `Windows`: ImmutableArray de `WindowGeometry`, somente com janelas principais visíveis e utilizáveis, em ordem Z da frente para trás; Z=0 é a primeira.
- Cada geometria contém HWND, Bounds (Left/Top/Right/Bottom), TopEdge, EdgeLeft, EdgeRight, Visibility e IsMaximized.
- `Changes`: somente o último lote de diferenças, com Before/After opcionais. Não é um histórico acumulado.
- `SnapshotChanged`: emitido apenas quando uma diferença é publicada. Consumidores podem usar Version para não reprocessar o último lote.
- `LastPollAtUtc`/`LastPollSucceeded`: saúde/frescor da consulta, mesmo quando a referência do snapshot não muda.

Mudanças possíveis: Appeared, Disappeared, Moved, Resized, Minimized, Restored, VisibilityChanged, ZOrderChanged e MaximizedChanged. Movimento e redimensionamento simultâneos são flags no mesmo delta, comparados entre geometrias visíveis. Uma restauração já fornece a nova geometria, mesmo quando não há um delta separado de movimento/tamanho em relação à posição anterior à minimização. Minimizadas/ocultas/cloaked ficam fora de Windows, mas seu estado anterior é mantido internamente para reconhecer restauração. Appeared/Disappeared indicam entrada/saída do conjunto de HWNDs principais acompanhados; ocultar/minimizar um HWND já conhecido produz mudança de visibilidade, e não um fechamento fictício. Reutilização de HWND por outro processo/thread produz desaparecimento e aparecimento.

Listas, dicionários, conjuntos e buffers são reutilizados. Se nada mudou, a mesma referência de snapshot e os mesmos arrays imutáveis são conservados. Falhas de enumeração não substituem um mapa completo por um parcial. Ao parar/descartar, o timer é interrompido; ao descartar, os buffers, estado atual e assinaturas são liberados.

**Coordenadas:** todos os valores do serviço são pixels físicos do desktop. Valores negativos e retângulos atravessando monitores são preservados sem divisão pelo DPI e sem limitar a geometria ao monitor do mascote. As bordas Right/Bottom são exclusivas. O minimapa é apenas uma apresentação escalada desses valores em WPF; não altera os dados. O retângulo DWM é o contorno geométrico da janela; a oclusão da borda é calculada separadamente pelo provider da Mark III.B.

### Diagnóstico opcional

Desativado por padrão. Ative **Diagnóstico de janelas** no menu do Smiley ou execute:

```powershell
dotnet run --project .\Smiley.Desktop.csproj -- --window-diagnostics
```

Uma janela própria mostra tabela de HWND, posição/tamanho, Z e maximização, além do minimapa das bordas e plataformas descrito acima. Não mostra nomes/títulos dos programas. Fechar o diagnóstico desmarca o menu; o serviço continua observando. Fechar o Smiley encerra também a janela de diagnóstico e o serviço. Não há arquivo de log, exportação automática, histórico persistente ou captura de tela no aplicativo.

Para inspeção por ferramentas de interface, também é possível usar `--window-diagnostics --inspect --paused`.

### Referências estudadas e decisão sobre hooks

Foram examinados [windows.ps1](https://github.com/mochiiwuff/capri-desktop/blob/main/src/main/windows.ps1), [windows.js](https://github.com/mochiiwuff/capri-desktop/blob/main/src/main/windows.js) e a [licença da Capri](https://github.com/mochiiwuff/capri-desktop/blob/main/LICENSE). Nenhum trecho foi copiado ou traduzido para este projeto. A implementação foi escrita diretamente para C#/WPF com base nas APIs oficiais; não usa o helper PowerShell/Node da Capri, reconhecimento de programas, leitura de títulos, dispositivos ou automação de outras janelas.

Documentação primária consultada:

- [EnumWindows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows).
- [DwmGetWindowAttribute](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute) e [DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute).
- [GetWindowRect: limites DWM não ajustados por DPI](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect).
- [IsIconic](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-isiconic), [GetWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindow) e [GetTopWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-gettopwindow).
- [Estilos estendidos](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles).
- [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook).

SetWinEventHook poderia reduzir latência de movimento/criação/minimização e evitar parte das consultas em um desktop parado. Ainda exigiria reconciliação periódica de ordem Z/estado, coalescência de muitos eventos durante arrastos, message loop, vida útil estável do delegate gerenciado e cuidado com reentrância. Para observação a 200 ms, isso não trouxe vantagem suficiente para introduzir hooks nesta etapa. Não foi registrado nenhum hook ou instalada dependência/instalador externo.

### Registro histórico da Mark III.A e limites do tracker

Restore e build: zero avisos e zero erros. **509 verificações passaram**, incluindo as anteriores da Mark II.C. Os novos testes verificam classificação, identidade, mudança geométrica/visibilidade, minimização/restauração (inclusive se a janela restaurada continuar cloaked), sobreposição/Z, coordenadas negativas e cenários de diferentes DPI sem conversão indevida. Também verificam reuso/imutabilidade dos snapshots, falha parcial, HWND reciclado, reentrância, timer independente e descarte.

Os testes nativos criam apenas janelas WPF de teste e conferem enumeração DWM real, movimento/redimensionamento, minimização, restauração, maximização, ocultação, exclusão do processo próprio, fechamento e uma janela destruída após ler sua geometria. A destruição durante a coleta usa um ponto de sincronização interno de teste; não interfere em janelas do usuário. Os testes de física, arremesso, animações e autonomia anteriores continuam passando. Os quatro arquivos responsáveis por esses sistemas são comparados com o registro de hashes `mark-iiia-preserved-code-hashes.json`; todos os PNGs continuam idênticos ao registro da Mark II.C.

Na validação visual com computer-use no Windows, o diagnóstico mostrou seis janelas, retângulos, maximização, Z e bordas superiores, sem títulos/conteúdo; também foi observado um delta Moved. Foi fechado apenas o diagnóstico e confirmado que o mascote permaneceu aberto. Isso não equivale a testar fisicamente configurações de vários monitores com escalas diferentes; esses cenários foram simulados nos testes e permanecem pendentes em hardware real.

Limitações conhecidas:

- A captura do desktop não é atômica no Windows. São dados consistentes de uma passagem, com validação final e snapshots imutáveis, mas outra janela pode mudar imediatamente depois de publicada. A próxima coleta reconcilia o mapa.
- Janelas criadas e destruídas inteiramente entre duas consultas podem não ser observadas. Reutilização de HWND pelo mesmo processo/thread entre consultas nem sempre permite distinguir todas as fases de vida.
- A classificação de janela principal é uma heurística por ownership, estilos e classes do shell. Overlays incomuns sem esses sinais podem exigir regras adicionais; diálogos owned são excluídos intencionalmente nesta versão.
- DWM indisponível/falhando exclui a janela do mapa ativo. Uma falha na coleta completa mantém o snapshot anterior; consumidores devem observar LastPollSucceeded/LastPollAtUtc se precisarem verificar frescor.
- O tracker fornece retângulos completos; o provider da Mark III.B calcula oclusão e segmentos livres separadamente. Cantos arredondados e regiões não retangulares continuam fora do escopo.

Arquivos novos na Mark III.A: `WindowTracker.cs`, `NativeWindowGeometrySource.cs`, `WindowTrackerDiagnosticWindow.cs`, `Tests/WindowTrackerChecks.cs` e `mark-iiia-preserved-code-hashes.json`. Naquela entrega, física, comportamento, estimador de arremesso e sprites estavam byte a byte preservados. A lista de alterações da Mark III.B está acima.

**Autonomia desligada é diferente de pausa:** desligar a autonomia deixa o movimento finito atual terminar e depois mantém repouso. A gravidade, arremesso, clique e Pular continuam funcionando. Pausar congela comportamento, decisões, cooldowns, reação e física, preservando o tempo e as velocidades para a retomada. Pular fica desabilitado no ar ou durante a pausa global.

## Decisões e prioridade

`SmileyBehaviorController` é uma camada pequena de decisão. Só solicita repouso, deslocamento finito e salto ao `SmileyController`, que continua sendo o único responsável por posição, animação, gravidade e colisão. A camada está ligada ao evento de renderização já existente; não há outro temporizador, thread ou loop independente.

As decisões/percepção usam uma cadência de 100 ms dentro do tempo real fornecido pela vista. O intervalo de movimento é dividido nesses instantes, sem depender do número de quadros renderizados. Não há sorteio por frame. Uma fonte Random é reutilizada; é possível fornecer sementes para reprodução dos testes. O controlador de movimento mantém o contrato antigo de uso isolado para os testes anteriores; ao ligar a camada de decisão, a agenda antiga é desativada, evitando duas autonomias simultâneas.

Comportamentos:

- **Idle:** pose neutra, descanso de 6–16 s. Descanso obrigatório após ações de deslocamento, tornando o repouso predominante.
- **Wander:** caminha por 2–4 s, com direção escolhida uma vez por ação e ajustada se não houver espaço.
- **Run:** corrida ocasional de 1–2 s, usando os oito quadros existentes.
- **Hop:** salto ocasional pela física existente, sem salto duplo; exige espaço vertical para o arco e tem cooldown de 20 s.
- **Curious:** cursor próximo pode interromper o deslocamento por 1–2 s, mostrando a pose desconfiada. Ao terminar, retoma modo, direção e tempo restante do deslocamento interrompido, quando aplicável.
- **Flee:** em até 25% dos encontros elegíveis de aproximação, corre por pouco tempo para longe do cursor se houver espaço. Fica pelo menos 8 s sem iniciar outra fuga. No modo Caminhar, a fuga respeita a preferência por caminhada.

No modo **Automático**, cada decisão espontânea tem pesos 55/25/10/10 para descanso/caminhada/corrida/salto. São probabilidades por decisão, não porcentagens do tempo. Como os descansos são longos, ocupam a maior parte da execução. Nos modos **Caminhar/Correr**, as decisões de deslocamento respeitam a preferência manual e não acrescentam saltos espontâneos. Reações ao cursor são eventos separados dessas decisões.

A prioridade é: controle/encerramento → pausa → arrasto → clique direto → condição aérea → reação ao cursor → decisão espontânea. A autonomia não interfere com queda ou arremesso, nem substitui uma reação ao clique. Após interação manual, aguarda o fim da reação/aterrissagem e pelo menos 2 s de tranquilidade antes de voltar a decidir.

## Cursor e parâmetros

A vista consulta somente `GetCursorPos`, até 10 vezes por segundo, enquanto a autonomia estiver ativa e não pausada. Nenhum clique externo é lido. A distância usa o retângulo do personagem e coordenadas físicas convertidas para DIPs por eixo. A camada identifica lado, distância e aproximação/afastamento. Uma nova reação exige um novo encontro: sair além de 162 DIPs e reentrar no raio de 120, respeitando o cooldown. Um cursor parado próximo não causa reações contínuas. A aproximação exige movimento do próprio cursor, evitando interpretar apenas a caminhada do Smiley como uma perseguição.

Há uma zona de proteção ao clique: dentro do retângulo da janela e de uma margem de 24 DIPs, a aproximação não dispara fuga. Clicar/arrastar tem prioridade imediata. Não há captura de tela, hooks globais, monitoramento de teclado, leitura de conteúdo/títulos de programas, acesso à internet, IA, notificações ou mudança de foco nas reações.

Parâmetros em `SmileyBehaviorOptions`, dentro de `SmileyBehaviorController.cs`:

| Opção | Inicial |
| --- | --- |
| IdleMin / IdleMax | 6 / 16 s |
| WanderMin / WanderMax | 2 / 4 s |
| RunMin / RunMax | 1 / 2 s |
| IdleWeight / WanderWeight / RunWeight / HopWeight | 55 / 25 / 10 / 10 |
| EvaluationInterval | 0,1 s, incluindo percepção |
| InterestRadius | 120 DIPs; rearmamento em 1,35× esse raio |
| CuriousMin / CuriousMax | 1 / 2 s |
| FleeProbability | 0,25 no máximo |
| FleeCooldown / CursorCooldown / HopCooldown | 8 / 5 / 20 s |
| ManualQuietSeconds | 2 s, depois de reação/aterrissagem |
| ClickZoneMargin / MinimumTravel / HopTopMargin | 24 / 24 / 12 DIPs |

## Física e chão

O próprio `SmileyController` guarda X/Y, VelocityX/VelocityY e contato com o chão ou uma plataforma. A condição física (`Grounded`, `Rising`, `Falling` ou `Suspended` durante o arrasto) é separada de Idle/Walking/Running/Reacting/Dragging. Assim, reagir enquanto cai troca a pose e avança o tempo da reação sem bloquear a gravidade.

O movimento agora acompanha `CompositionTarget.Rendering`, com limite de **60 atualizações/s**, substituindo o DispatcherTimer de 33 ms (~30 Hz). Eventos duplicados do mesmo quadro são ignorados e a fração de tempo é preservada em monitores com atualização superior a 60 Hz. A física continua usando o tempo real do Stopwatch, sem acelerar o arremesso ou modificar a frequência dos sprites. Não há reprodução de quadros atrasados; a assinatura do evento é removida no fechamento. A taxa efetiva depende da renderização do WPF, da tela e da carga da máquina; 60 FPS constantes não foram medidos.

Para gravidade constante, cada intervalo integra `y += vy * dt + 0,5 * g * dt²` e `vy += g * dt`. É uma solução balística exata, sem motor de física ou passos de posição pré-gravados. O intervalo é dividido nos instantes de aterrissagem, limite horizontal e mudança de comportamento. O personagem encosta exatamente no chão, zera a velocidade vertical e consome o tempo restante no comportamento de solo, mesmo após uma atualização longa. O ciclo de renderização usa o tempo real do Stopwatch; o limite antigo de 250 ms por atualização foi removido. Não há thread ou temporizador adicional.

O salto pelo menu aplica uma velocidade inicial para cima somente se houver apoio no chão ou numa plataforma e a simulação estiver ativa. Com os valores padrão, o ápice ocorre após aproximadamente **0,418 s**, a altura máxima é **96,18 DIPs** e o retorno à altura inicial ocorre após **0,836 s**, salvo aterrissagem anterior em outra superfície. Não há salto duplo nem atalhos globais. O salto do menu é vertical; arremessos pelo mouse também têm velocidade horizontal. A alternância de quadros fica suspensa no ar. Na subida usa a pose assustada; na descida usa **Mascote Sombra em Queda.png**, carregada e congelada uma única vez, com a máscara alfa correspondente. Uma reação continua usando as poses da Mark I sem cancelar o impulso do arremesso.

Ao começar o arrasto, ambas as velocidades são zeradas. O mouse mantém o deslocamento relativo ao ponto inicial; a física fica suspensa. `DragVelocityTracker.cs` mede os últimos 120 ms de movimento da posição real e limitada do personagem em pixels físicos. Ao soltar, essa velocidade é transferida para o controlador, sem salto de posição. Abaixo de 180 DIPs/s, ou após segurar parado por 120 ms, o personagem apenas cai. O impulso total é limitado a 1.100 DIPs/s. Colisões com bordas laterais absorvem a velocidade horizontal e mantêm a queda; o chão absorve o impulso ao aterrissar. Não há ricochete ou física avançada. Perder a captura cancela o arrasto sem arremesso. A autonomia espera a aterrissagem. Enquanto o menu ou uma captura do mouse estiverem ativos, o tempo não é acumulado para gerar um salto posterior.

O chão vem do limite inferior de `GetMonitorInfo.rcWork`, que já desconta a barra de tarefas do Windows na posição configurada. A vista calcula um deslocamento físico até a referência dos pés no canvas, em vez de usar o tamanho do PNG original. Os limites horizontais continuam no monitor atual; arrastar permite mudar de monitor. Redimensionar a área útil reposiciona quem já está apoiado e preserva o momento de quem está no ar, salvo se o novo chão já interceptar o personagem.

**Unidades:** X/Y, dimensões da janela, área útil e velocidades internas usam pixels físicos. Speed, RunSpeed e JumpSpeed são convertidos de DIPs/s pelo DPI correspondente; Gravity, de DIPs/s². A escala X afeta o deslocamento horizontal, a escala Y afeta salto/gravidade. Uma mudança de DPI conserva a velocidade vertical em DIPs/s. A física usa o mesmo sistema de coordenadas das APIs existentes.

## Novos quadros

- `SmileyImagens/Corrida/frame_01.png` até `frame_08.png`: corrida.
- `SmileyImagens/Caminhada/frame_01.png` até `frame_08.png`: caminhada.
- Todos os quadros são PNG BGRA de **444 × 444**, com transparência.
- `exports/Smiley-Corrida-8-frames.zip` contém os oito PNGs de corrida.
- `frames.json` em cada pasta registra a origem, hash SHA-256, retângulos da grade, preenchimento e referência comum.

As fontes medem 1774 × 887. Os limites proporcionais de X são **0, 444, 887, 1331, 1774**; os de Y são **0, 444, 887**. Assim, cada pixel pertence a uma única célula. A leitura segue esquerda → direita, linha superior → inferior. As células têm 443 ou 444 pixels; o preenchimento transparente na direita/parte inferior completa os 444 pixels. Não há escala, interpolação, alteração de cores ou corte individual da silhueta na exportação. Todos os pixels com alfa diferente de zero são preservados, incluindo os quase transparentes.

A exportação mantém a mesma origem para a grade inteira. Não se recentraliza horizontalmente o retângulo de cada personagem conforme braços/pernas se movem. Na Mark II.B, o carregamento de apresentação calcula uma vez a linha visível dos pés por quadro (alfa > 8, o mesmo limiar da máscara) e ajusta somente a translação vertical em memória para a linha comum **Y=190 DIPs**. A estrutura Alignment da Mark II.A é reutilizada, mantendo GroundY − TopY = 1: o fator de escala é o mesmo nos 16 quadros. Nenhum PNG, inclusive os exports, foi alterado. Os testes verificam contato a até 0,5 DIP na imagem interna e até dois pixels físicos na máscara nas escalas testadas. As variações internas do desenho podem causar pequenas diferenças de altura do corpo; não há deformação ou squash-and-stretch.

As duas folhas originais e todos os demais PNGs existentes foram preservados. Os antigos arquivos de movimento que o usuário havia removido não são mais exigidos. O antigo `Caminhada_2.png`, que não era mais usado, foi removido externamente durante a implementação da Mark II.C. Não foi recriado. Os registros históricos de hashes continuam preservados e os testes relatam essa ausência explicitamente; todos os 32 PNGs presentes no registro da Mark II.C são verificados sem exceções. O PNG de queda continua sendo usado como pose estática na descida, sem alterar o arquivo.

Para reproduzir a exportação, usando apenas o WPF instalado:

```powershell
.\Tools\Export-SpriteSheets.ps1
```

Esse comando regrava somente os arquivos derivados `frame_*.png` e `frames.json` nas duas pastas de ciclos. Não altera as folhas originais. O ZIP pode ser recriado com `Compress-Archive` sobre os PNGs de corrida.

## Base preservada: código e animação da Mark II.C

- `SmileyController.cs` (alterado na Mark II.C, preservado na III.A): execução de ações finitas, API de repouso/deslocamento e separação da agenda externa. Física, gravidade, limites e arremesso mantidos. `FramesPerCycle = 8`.
- `SmileyBehaviorController.cs` (novo): ações espontâneas, preferências, probabilidades, percepção do cursor, cooldowns, curiosidade, fuga e prioridades.
- `SpriteManager.cs` (modificado): carrega os quadros uma única vez e deriva a referência visual dos pés, com alinhamento em memória. Os PNGs exportados permanecem sem redimensionamento.
- `MainWindow.xaml` / `.xaml.cs` (base da Mark II.C): menu de autonomia/Automático, consulta limitada do cursor e integração da camada de decisão com renderização e interações existentes; a integração do WindowTracker está descrita acima.
- `NativeMethods.cs`: limites da área útil e regiões de transparência. O cache continua separando imagem, dimensões físicas e orientação; não é recalculado em atualizações sem mudança de quadro.
- `Smiley.Desktop.csproj`: copia os PNGs recursivamente, incluindo as pastas dos novos ciclos, para saída/publicação.
- `Tools/Export-SpriteSheets.ps1`: exportação determinística das duas grades.
- `Tests/Program.cs` (modificado): testes do controlador, imagens, exportação e regiões nativas; os cenários que antes caminhavam no ar agora começam no chão.
- `Tests/PhysicsChecks.cs` (novo): trajetórias, contato, salto duplo, pausa, arrasto, DPI e teste dos comandos da janela WPF real.
- `DragVelocityTracker.cs`: histórico curto de posições e estimativa da velocidade de soltura.
- `Tests/ThrowChecks.cs`: estimativa do impulso, limites, trajetória, reação, pausa, DPI, aterrissagem e carregamento da pose de queda.
- `Tests/BehaviorChecks.cs` (novo): seleção/distribuição, sementes, temporização, prioridades, proximidade, cooldowns, modos, autonomia desligada e integração dos novos comandos WPF.
- `Tests/ImageIntegrity.cs` (novo): comparação histórica com exceção explícita somente para o antigo Caminhada_2 removido externamente; nenhum outro arquivo ausente é aceito.
- `Tests/Program.cs` e `Tests/PhysicsChecks.cs` (base da Mark II.C): integração dos testes comportamentais e relato da retirada do arquivo antigo.
- `mark-iic-image-hashes.json`: registro estrito dos 32 PNGs originais/derivados presentes desde a Mark II.C.
- `mark-iib-image-hashes.json` (novo): hashes SHA-256 de todos os PNGs, originais e derivados, antes da implementação da física.
- `README.md` e `test-results.txt` (atualizados): documentação e resultado da execução.
- `sprite-sheet-source-hashes.json`: registro dos arquivos presentes antes desta migração. Os registros históricos da Mark I e II.A foram mantidos.

A animação avança pelo tempo real do deslocamento no chão. Reação, pausa, arrasto, repouso e condição aérea interrompem o ciclo; selecionar novamente o modo atual não o reinicia. Ao parar no chão, retorna à pose neutra. A direção espelha o canvas inteiro, mantendo sua referência. A pausa conserva o índice e tempo restantes; a aterrissagem reinicia a sequência de deslocamento no primeiro quadro.

Ajustes em `SmileyOptions`, dentro de `SmileyController.cs`:

| Opção | Inicial |
| --- | --- |
| Speed | 65 DIPs/s, caminhada |
| RunSpeed | 140 DIPs/s, corrida |
| WalkFramesPerSecond | 4 FPS: ciclo de oito quadros em 2 s |
| RunFramesPerSecond | 8 FPS: ciclo de oito quadros em 1 s |
| IdleMin / IdleMax | 3 / 7 s, somente agenda legada de uso isolado do controlador |
| WalkMin / WalkMax | 2 / 5 s, somente agenda legada de uso isolado do controlador |
| ReactionSeconds | 1,1 s |
| Gravity | 1.100 DIPs/s² |
| JumpSpeed | 460 DIPs/s para cima |
| ThrowMinimumSpeed | 180 DIPs/s: velocidade mínima para arremessar |
| ThrowMaximumSpeed | 1.100 DIPs/s: limite da magnitude do impulso |

Os quadros mantêm o mesmo fator de escala na exibição, sem ajustar o tamanho individual de cada silhueta. O canvas de apresentação é 210 × 190 DIPs. A rasterização de exibição é feita uma única vez em 4×; escalas acima de 400% podem perder nitidez. Os streams são fechados após o carregamento. O cache de regiões GDI é liberado ao fechar.

## Validação anterior da Mark II.C (histórico)

```powershell
dotnet run --project .\Tests\Smiley.Tests.csproj -- C:\dev\Smiley
```

Antes da Mark II.C, as **442 verificações existentes passaram**. Após aquela implementação, **477 verificações passaram**, com casos comportamentais agrupados em cenários relevantes. O resultado mais recente da Mark III.A está em `test-results.txt` e na seção de validação acima. Restore e build passaram, sem avisos nem erros, no SDK 10.0.302. A retirada externa do arquivo não utilizado é relatada como INFO nos testes históricos; o registro de imagens exige presença e hash idêntico de todos os 32 PNGs.

Os novos testes cobrem distribuição 55/25/10/10 em 10 mil decisões com semente fixa, reprodução de uma sequência completa de ações/física, equivalência de 90 s simulados de uma vez ou a 60 Hz, ausência de sorteio por frame e predominância de repouso numa simulação longa. Também verificam descanso entre ações, salto seguro/cooldown, curiosidade com retomada, lado/aproximação/afastamento do cursor, zona segura de clique, cooldowns, fuga sem ultrapassar bordas, prioridade de arrasto/clique/arremesso, pausa, autonomia desligada, modos manuais e percepção em monitor de coordenadas negativas a 200% de DPI. Não há um teste novo por frame simulado.

Os testes WPF acionam os comandos reais da MainWindow e verificam seleção exclusiva Automático/Caminhar/Correr e disponibilidade de Pular com autonomia desativada. A física e as verificações de imagens, máscaras, ciclo e arremesso anteriores continuam executando.

Os testes de física verificam aceleração, altura/tempo do ápice, subida/descida, contato exato após intervalos longos, bloqueio de salto duplo, queda após arrasto sem impulso do mouse, pausa/retomada com velocidade preservada, reação durante a queda, equivalência entre 30/60/144 Hz e intervalos irregulares, limites negativos/horizontais, área útil alterada, referência explícita de pés, retorno das duas animações após aterrissar e configurações físicas inválidas. Há cenários de salto/queda em 100%, 125%, 150% e 200% de DPI.

Um teste de integração cria uma MainWindow WPF real e aciona seus eventos de menu, verificando Pular desabilitado durante a pausa, habilitado após retomar, troca para a pose aérea e rejeição do segundo salto. Isso é um teste automatizado dos comandos; não equivale a uma observação visual contínua da trajetória.

As verificações anteriores continuam cobrindo os pixels RGBA dos 16 PNGs exportados, ordem da grade, transparência, hashes, ciclos, velocidades, reação, cache e espelhamento. As máscaras agora também verificam proximidade dos pés com o chão em 100%, 125%, 150% e 200%. Todos os PNGs, inclusive os derivados, permanecem idênticos ao registro do início da Mark II.B.

Na sessão Windows da Mark II.C, a ferramenta computer-use observou inicialização, repouso neutro, expressão desconfiada quando o cursor foi aproximado e o novo item de autonomia marcado no menu, seguido de retorno à pose neutra. A inspeção usou `--inspect`, que habilita a barra de tarefas somente para facilitar a ferramenta. Os modos, cooldowns, distribuição, fuga e saltos espontâneos foram validados por código; não foi feita uma observação visual longa de todas as ações. A física já havia sido validada manualmente pelo usuário antes desta etapa; isso não substitui uma validação visual completa da nova autonomia.

## Limitações e teste manual

- Executar, selecionar Pular no chão e observar um único arco; tentar repetir durante o voo. Arrastar para cima e soltar para conferir a queda. Pausar/retomar no ar pelo menu deve preservar a velocidade.
- Conferir visualmente os ciclos com os pés apoiados e a aparência das trocas de poses; os desenhos independentes ainda contêm diferenças internas.
- Permanecem pendentes testes físicos de múltiplos monitores/DPI mistos, mudança real de resolução/barra de tarefas, alternância de sempre visível e passagem efetiva de cliques para diferentes aplicativos. A geometria das regiões transparentes e os limites são testados automaticamente.
- O salto do menu é vertical; o arremesso segue o movimento recente do mouse. Há colisão descendente com plataformas de janelas; não há colisão com suas laterais, teto, ícones ou cursor. Uma soltura rápida para cima pode levar temporariamente parte do personagem acima da área útil; a gravidade o traz de volta. Numa área útil menor que o personagem, a parte superior pode ficar fora da área, mantendo os pés no chão.
- Autonomia e preferências não são persistidas entre execuções; o aplicativo começa em Automático com autonomia ligada. Os encontros são estimados apenas por posição do cursor e retângulo da janela, sem análise de conteúdo ou silhueta detalhada para percepção.
- As decisões são quantizadas em 100 ms para reduzir trabalho e preservar independência da renderização. Cooldowns e duração de observação seguem essa precisão.
- Não há física avançada nem movimento controlado por IA. A conversa com Ollama da Mark IV.B está descrita acima; a identidade oficial da Mark IV.C não foi implementada. Nenhum PNG foi criado, alterado, removido ou renomeado pelo agente nesta etapa.
