using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Smiley;

public partial class SmileyChatWindow : Window
{
    private readonly IChatProvider demonstration = new MockChatProvider();
    private readonly OllamaChatProvider catalog = new();
    private readonly Action<string> speak;
    private readonly SmileyActionDispatcher? actions;
    private readonly Action<bool>? setActionsEnabled;
    private readonly CancellationTokenSource lifetime = new();
    private sealed record Entry(string Role, string Content, string Label, bool Simulated = false);
    private readonly Queue<Entry> recent = new();
    private ChatSettings settings = ChatSettingsStore.Load();
    private CancellationTokenSource? activeRequest, listRequest;
    private int responseVersion, listVersion;
    private bool initializing = true, selectingModel, refreshing, closed;
    private bool IsOllama => ProviderPicker.SelectedIndex == 1;
    private OllamaModel? SelectedModel => ModelPicker.SelectedItem as OllamaModel;

    public SmileyChatWindow(Action<string> speak, SmileyActionDispatcher? actions = null, Action<bool>? setActionsEnabled = null)
    {
        InitializeComponent(); this.speak = speak; this.actions = actions; this.setActionsEnabled = setActionsEnabled;
        ProviderPicker.SelectedIndex = settings.Provider == ChatProviderKind.Ollama ? 1 : 0;
        UpdateProviderView(); initializing = false;
        Loaded += async (_, _) => { FocusInput(); if (IsOllama) await RefreshModelsAsync(); };
        Closed += (_, _) =>
        {
            closed = true; responseVersion++; listVersion++;
            lifetime.Cancel(); activeRequest?.Cancel(); listRequest?.Cancel(); lifetime.Dispose();
        };
    }
    public void FocusInput()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (!closed) { Input.Focus(); Keyboard.Focus(Input); } });
    }
    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBlock or System.Windows.Controls.Grid or System.Windows.Controls.StackPanel) DragMove();
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }
    private async void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true; await SubmitAsync();
    }
    private async void OnSend(object sender, RoutedEventArgs e) => await SubmitAsync();
    private void AddHistory(Entry entry)
    {
        recent.Enqueue(entry);
        while (recent.Count > 8) recent.Dequeue();
        History.Text = string.Join("\n\n", recent.Select(e => e.Label + ": " + e.Content)); HistoryScroll.ScrollToEnd();
    }

    private void UpdateReady()
    {
        Send.IsEnabled = !closed && activeRequest is null && (!IsOllama || !refreshing && SelectedModel is not null);
        CancelReply.Visibility = activeRequest is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshModels.IsEnabled = !refreshing; ModelPicker.IsEnabled = !refreshing;
    }
    private void UpdateDescription()
    {
        ProviderDescription.Text = IsOllama
            ? "Ollama local · " + (SelectedModel?.Name ?? "selecione um modelo instalado")
            : "Demonstração local · respostas simuladas";
        UpdateActionControl();
    }
    private void UpdateActionControl()
    {
        AllowAiActions.IsEnabled = actions is not null && setActionsEnabled is not null;
        AllowAiActions.IsChecked = actions?.Enabled == true;
        ActionControlDescription.Text = !IsOllama ? "Demonstração: somente conversa, sem executar ações."
            : actions?.Enabled == true ? "Ações por IA ativadas · pedidos sujeitos à física e às opções do menu."
            : "Somente conversa · ative a opção acima para pedir movimentos.";
    }
    private void OnAllowAiActions(object sender, RoutedEventArgs e) => setActionsEnabled?.Invoke(AllowAiActions.IsChecked == true);
    private void UpdateProviderView()
    {
        ModelControls.Visibility = IsOllama ? Visibility.Visible : Visibility.Collapsed;
        UpdateDescription();
        ConnectionStatus.Text = IsOllama ? "Aguardando consulta ao Ollama local…" : "Demonstração: frases simuladas, sem conexão externa.";
        ConnectionStatus.Foreground = Brushes.LightGray; UpdateReady();
    }
    private void SaveChoice()
    {
        settings = ChatSettingsStore.Load() with { Provider = IsOllama ? ChatProviderKind.Ollama : ChatProviderKind.Demonstration, Model = SelectedModel?.Name ?? settings.Model };
        if (!ChatSettingsStore.Save(settings)) Status.Text = "Escolha aplicada, mas não foi possível salvá-la.";
    }
    private void CancelResponse(bool showStatus)
    {
        responseVersion++; activeRequest?.Cancel(); activeRequest = null;
        if (!closed) { UpdateReady(); if (showStatus) Status.Text = "Solicitação cancelada."; }
    }
    private void CancelModelLoad()
    {
        listVersion++; listRequest?.Cancel(); listRequest = null; refreshing = false;
    }
    private void OnCancelReply(object sender, RoutedEventArgs e) => CancelResponse(true);
    public void NotifyActionControlChanged()
    {
        CancelResponse(false);
        UpdateActionControl();
        Status.Text = actions?.Enabled == true ? "Ações por IA ativadas para novos pedidos ao Ollama." : "Ações por IA desativadas; a conversa continua disponível.";
    }
    private async void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || closed) return;
        CancelResponse(false); CancelModelLoad(); UpdateProviderView(); SaveChoice();
        Status.Text = "Enter envia · Shift+Enter quebra a linha";
        if (IsOllama) await RefreshModelsAsync();
    }
    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || selectingModel || closed) return;
        CancelResponse(false); UpdateDescription(); SaveChoice();
        Status.Text = SelectedModel is null ? "Selecione um modelo local." : "Modelo alterado. O histórico foi preservado.";
    }
    private async void OnRefreshModels(object sender, RoutedEventArgs e) => await RefreshModelsAsync();
    private async Task RefreshModelsAsync()
    {
        if (closed || !IsOllama) return;
        CancelResponse(false); CancelModelLoad();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        int version = ++listVersion; listRequest = cancellation; refreshing = true;
        selectingModel = true; ModelPicker.ItemsSource = null; selectingModel = false;
        ConnectionStatus.Text = "Consultando localhost:11434…"; ConnectionStatus.Foreground = Brushes.LightGray;
        UpdateDescription(); UpdateReady();
        try
        {
            var result = await catalog.GetModelsAsync(cancellation.Token);
            if (closed || version != listVersion || !IsOllama) return;
            selectingModel = true;
            ModelPicker.ItemsSource = result.Models;
            ModelPicker.SelectedItem = result.Models.FirstOrDefault(m => m.Name == settings.Model);
            selectingModel = false;
            ConnectionStatus.Foreground = new SolidColorBrush(Color.FromRgb(162, 205, 174));
            ConnectionStatus.Text = result.Models.Count == 0
                ? "Disponível, sem modelos locais. Instale um modelo pelo Ollama (ollama pull NOME_DO_MODELO) e clique em Atualizar."
                : $"Disponível · {result.Models.Count} modelos locais. " + (SelectedModel is null ? "Selecione um para conversar." : "Pronto para conversar.");
            if (result.RemoteModelsExcluded > 0) ConnectionStatus.Text += $" {result.RemoteModelsExcluded} modelos remotos/sem pesos locais foram omitidos.";
            UpdateDescription();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (ChatProviderException error)
        {
            if (closed || version != listVersion) return;
            ConnectionStatus.Foreground = new SolidColorBrush(Color.FromRgb(230, 173, 156));
            ConnectionStatus.Text = (error.ConnectionUnavailable ? "Indisponível · " : "Falha na consulta · ") + error.Message;
        }
        catch (Exception)
        {
            if (!closed && version == listVersion) ConnectionStatus.Text = "Não foi possível consultar os modelos do Ollama.";
        }
        finally
        {
            if (ReferenceEquals(listRequest, cancellation)) { listRequest = null; refreshing = false; if (!closed) UpdateReady(); }
        }
    }

    private async Task SubmitAsync()
    {
        string message = Input.Text.Trim();
        if (activeRequest is not null || closed || message.Length == 0 || IsOllama && (refreshing || SelectedModel is null)) return;
        IChatProvider provider = IsOllama ? new OllamaChatProvider(SelectedModel!.Name) : demonstration;
        string label = IsOllama ? "Smiley · Ollama / " + SelectedModel!.Name : "Smiley · demonstração";
        bool simulated = !IsOllama;
        bool requestActions = IsOllama && actions?.Enabled == true;
        long actionRevision = actions?.Revision ?? 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        int version = ++responseVersion; activeRequest = cancellation;
        UpdateReady(); Input.Clear();
        Input.Focus();
        Status.Text = IsOllama ? "Respondendo… O primeiro carregamento pode demorar." : "Preparando resposta simulada…";
        AddHistory(new("user", message, "Você"));
        var request = new ChatRequest(SmileyIdentity.SystemInstruction, recent.Select(e => new ChatMessage(e.Role,
            e.Simulated ? "[Resposta simulada da demonstração] " + e.Content : e.Content)).ToArray(),
            RequestActions: requestActions, ActionContext: requestActions ? actions!.DescribeState() : null);
        try
        {
            string reply = await provider.GetReplyAsync(request, cancellation.Token);
            if (closed || version != responseVersion || cancellation.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(reply)) { Status.Text = "Sem resposta. Tente outra mensagem."; return; }
            string? actionNote = null;
            if (requestActions)
            {
                var response = SmileyAgentResponse.Parse(reply);
                reply = response.Reply;
                if (response.ValidationMessage is not null) actionNote = response.ValidationMessage;
                else
                {
                    var result = actions!.Dispatch(response.Action, actionRevision);
                    if (result.Message.Length > 0) actionNote = result.Accepted ? result.Message : "Não executada: " + result.Message;
                }
            }
            AddHistory(new("assistant", actionNote is null ? reply : reply + "\n[" + actionNote + "]", label, simulated));
            speak(actionNote is null ? reply : reply + "\n" + actionNote);
            Status.Text = actionNote ?? (requestActions ? "O modelo não solicitou ação nesta resposta."
                : IsOllama ? "Somente conversa · ações por IA desativadas." : "Enter envia · Shift+Enter quebra a linha");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (ChatProviderException error)
        {
            if (closed || version != responseVersion) return;
            Status.Text = error.Message;
            if (error.ConnectionUnavailable) { ConnectionStatus.Text = "Indisponível · " + error.Message; ConnectionStatus.Foreground = Brushes.Salmon; }
        }
        catch (Exception) { if (!closed && version == responseVersion) Status.Text = "Não consegui responder. Tente novamente."; }
        finally
        {
            if (ReferenceEquals(activeRequest, cancellation)) { activeRequest = null; if (!closed) UpdateReady(); }
        }
    }
}
