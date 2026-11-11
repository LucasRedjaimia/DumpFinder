using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WinCrashDoctor
{
    /// <summary>
    /// AI provider card and MCP integration card. Both are hosted by the Settings page;
    /// the AI itself is used from each crash's detail panel (MainWindow.CrashesPage.cs).
    /// </summary>
    public partial class MainWindow : Window
    {
        // AI provider
        private Border _aiProviderCard;
        private TextBlock _lblAiProviderCaption;
        private ComboBox _cbAiProvider;
        private ComboBox _cbClaudeModel;
        private TextBlock _lblAiCredentialCaption;
        private Border _bdAiApiKeyWrap;
        private PasswordBox _pbAiApiKey;
        private Border _bdOllamaModelWrap;
        private TextBox _txtOllamaModel;
        private Button _btnCopyAiPrompt;

        // MCP server
        private StackPanel _mcpCardStack;
        private TextBlock _lblMcpCardTitle;
        private Border _bdMcpStatusBadge;
        private TextBlock _lblMcpStatusBadge;
        private TextBlock _lblMcpCardDesc;
        private TextBox _txtMcpConfigPreview;
        private Button _btnRegisterAntigravity;
        private Button _btnRegisterClaudeDesktop;
        private Button _btnCopyClaudeMcp;
        private Button _btnCopyGeminiMcp;

        private void BuildAiPage()
        {
            BuildAiProviderCard();
            BuildMcpCard();
            UpdateAiCredentialVisibility();
            RefreshMcpRegistrationStatus();
        }

        private void BuildAiProviderCard()
        {
            _aiProviderCard = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 14, 18, 16)
            }, "BgCard"), "BorderCard");

            Grid cfgGrid = new Grid();
            cfgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(225) });
            cfgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            cfgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cfgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            cfgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel provCol = new StackPanel();
            _lblAiProviderCaption = ResFg(new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 4) }, "TextMuted");
            _cbAiProvider = new ComboBox();
            _cbAiProvider.Items.Add("Google Gemini");
            _cbAiProvider.Items.Add("Anthropic Claude");
            _cbAiProvider.Items.Add("Ollama (local)");
            int savedProv = (Localizer.CachedSettings != null && Enum.IsDefined(typeof(AiProviderType), Localizer.CachedSettings.AiProvider))
                ? Localizer.CachedSettings.AiProvider
                : 0;
            _cbAiProvider.SelectedIndex = savedProv;
            _cbAiProvider.SelectionChanged += (s, e) =>
            {
                if (_suppressDropdownEvents || _cbAiProvider.SelectedIndex < 0) return;
                SaveAiSettingsFromUi();
                UpdateAiCredentialVisibility();
            };
            provCol.Children.Add(_lblAiProviderCaption);
            provCol.Children.Add(_cbAiProvider);

            // Claude model (shown only for Anthropic)
            _cbClaudeModel = new ComboBox { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var m in ClaudeAdvisor.Models) _cbClaudeModel.Items.Add(m[1]);
            string savedModel = Localizer.CachedSettings != null ? Localizer.CachedSettings.ClaudeModel : null;
            int mi = Array.FindIndex(ClaudeAdvisor.Models, m => m[0] == savedModel);
            _cbClaudeModel.SelectedIndex = mi >= 0 ? mi : 0;
            _cbClaudeModel.SelectionChanged += (s, e) => { if (!_suppressDropdownEvents) SaveAiSettingsFromUi(); };
            provCol.Children.Add(_cbClaudeModel);
            cfgGrid.Children.Add(provCol);

            StackPanel credCol = new StackPanel();
            _lblAiCredentialCaption = ResFg(new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 4) }, "TextMuted");
            credCol.Children.Add(_lblAiCredentialCaption);

            _bdAiApiKeyWrap = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 5, 9, 5)
            }, "BgInput"), "BorderCard");
            _pbAiApiKey = ResFg(new PasswordBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Password = (Localizer.CachedSettings != null && Localizer.CachedSettings.AiApiKey != null) ? Localizer.CachedSettings.AiApiKey : ""
            }, "TextPrimary");
            _pbAiApiKey.PasswordChanged += (s, e) => SaveAiSettingsFromUi();
            _bdAiApiKeyWrap.Child = _pbAiApiKey;
            credCol.Children.Add(_bdAiApiKeyWrap);

            _bdOllamaModelWrap = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 5, 9, 5),
                Visibility = Visibility.Collapsed
            }, "BgInput"), "BorderCard");
            _txtOllamaModel = ResFg(new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Text = (Localizer.CachedSettings != null && !string.IsNullOrEmpty(Localizer.CachedSettings.OllamaModel)) ? Localizer.CachedSettings.OllamaModel : "llama3.2"
            }, "TextPrimary");
            _txtOllamaModel.TextChanged += (s, e) => SaveAiSettingsFromUi();
            _bdOllamaModelWrap.Child = _txtOllamaModel;
            credCol.Children.Add(_bdOllamaModelWrap);
            Grid.SetColumn(credCol, 2);
            cfgGrid.Children.Add(credCol);

            _btnCopyAiPrompt = CreateFlatButton("", "AccentSoftBg", "AccentText", "AccentSoftBg", new Thickness(13, 7, 13, 7), 12);
            _btnCopyAiPrompt.VerticalAlignment = VerticalAlignment.Bottom;
            _btnCopyAiPrompt.Click += (s, e) => OnCopyAiPromptClick();
            Grid.SetColumn(_btnCopyAiPrompt, 4);
            cfgGrid.Children.Add(_btnCopyAiPrompt);

            _aiProviderCard.Child = cfgGrid;
        }

        private void BuildMcpCard()
        {
            _mcpCardStack = new StackPanel();
            Border mcpCard = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 16, 18, 16),
                Margin = new Thickness(0, 0, 0, 14)
            }, "BgCard"), "BorderCard");

            StackPanel mcpInner = new StackPanel();
            Grid mcpHdr = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            mcpHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mcpHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblMcpCardTitle = ResFg(new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }, "TextPrimary");
            mcpHdr.Children.Add(_lblMcpCardTitle);
            _bdMcpStatusBadge = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center };
            _lblMcpStatusBadge = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold };
            _bdMcpStatusBadge.Child = _lblMcpStatusBadge;
            Grid.SetColumn(_bdMcpStatusBadge, 1);
            mcpHdr.Children.Add(_bdMcpStatusBadge);
            mcpInner.Children.Add(mcpHdr);

            _lblMcpCardDesc = ResFg(new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }, "TextSecondary");
            mcpInner.Children.Add(_lblMcpCardDesc);

            Border codeWrap = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 12)
            }, "BgInput"), "BorderCard");
            _txtMcpConfigPreview = ResFg(new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                FontFamily = Ds.MonoFont,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            }, "TextSecondary");
            codeWrap.Child = _txtMcpConfigPreview;
            mcpInner.Children.Add(codeWrap);

            WrapPanel mcpActions = new WrapPanel { Orientation = Orientation.Horizontal };
            _btnRegisterAntigravity = CreateFlatButton("", "Accent", "OnAccent", "Accent", new Thickness(14, 7, 14, 7), 12);
            _btnRegisterAntigravity.Margin = new Thickness(0, 0, 8, 6);
            _btnRegisterAntigravity.Click += (s, e) => OnRegisterAntigravityClick();
            _btnCopyClaudeMcp = CreateFlatButton("", "BgSubtle", "TextPrimary", "BorderCard", new Thickness(13, 7, 13, 7), 12);
            _btnCopyClaudeMcp.Margin = new Thickness(0, 0, 8, 6);
            _btnCopyClaudeMcp.Click += (s, e) => CopyToClipboard(McpServer.GetClaudeCodeCommand());
            _btnCopyGeminiMcp = CreateFlatButton("", "BgSubtle", "TextPrimary", "BorderCard", new Thickness(13, 7, 13, 7), 12);
            _btnCopyGeminiMcp.Margin = new Thickness(0, 0, 0, 6);
            _btnCopyGeminiMcp.Click += (s, e) => CopyToClipboard(McpServer.GetGeminiCliCommand());
            _btnRegisterClaudeDesktop = CreateFlatButton("", "Accent", "OnAccent", "Accent", new Thickness(14, 7, 14, 7), 12);
            _btnRegisterClaudeDesktop.Margin = new Thickness(0, 0, 8, 6);
            _btnRegisterClaudeDesktop.Click += (s, e) => OnRegisterClaudeDesktopClick();
            mcpActions.Children.Add(_btnRegisterClaudeDesktop);
            mcpActions.Children.Add(_btnRegisterAntigravity);
            mcpActions.Children.Add(_btnCopyClaudeMcp);
            mcpActions.Children.Add(_btnCopyGeminiMcp);
            mcpInner.Children.Add(mcpActions);

            mcpCard.Child = mcpInner;
            _mcpCardStack.Children.Add(mcpCard);
        }

        private void CopyToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text ?? "");
                SetToast(Localizer.L("Copié : ", "Copied: ", "Copiado: ", "Kopiert: ", "Copiato: ", "Copiado: ") + text, "SuccessFg");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Clipboard");
                SetToast(Localizer.L("Le presse-papiers est occupé, réessayez.", "Clipboard busy, try again.", "Portapapeles ocupado.", "Zwischenablage belegt.", "Appunti occupati.", "Área de transferência ocupada."), "WarnFg");
            }
        }

        private void UpdateAiCredentialVisibility()
        {
            if (_cbAiProvider == null) return;
            bool isOllama = (_cbAiProvider.SelectedIndex == (int)AiProviderType.OllamaLocal);
            _bdAiApiKeyWrap.Visibility = isOllama ? Visibility.Collapsed : Visibility.Visible;
            _bdOllamaModelWrap.Visibility = isOllama ? Visibility.Visible : Visibility.Collapsed;
            if (_cbClaudeModel != null) _cbClaudeModel.Visibility = _cbAiProvider.SelectedIndex == (int)AiProviderType.Claude ? Visibility.Visible : Visibility.Collapsed;

            if (isOllama)
                _lblAiCredentialCaption.Text = Localizer.L("Modèle Ollama local", "Local Ollama model", "Modelo Ollama local", "Lokales Ollama-Modell", "Modello Ollama locale", "Modelo Ollama local");
            else if (_cbAiProvider.SelectedIndex == (int)AiProviderType.Claude)
                _lblAiCredentialCaption.Text = Localizer.L("Clé API Anthropic Claude (stockée localement)", "Anthropic Claude API Key (stored locally)", "Clave API Anthropic Claude", "Anthropic Claude API-Schlüssel", "Chiave API Anthropic Claude", "Chave API Anthropic Claude");
            else
                _lblAiCredentialCaption.Text = Localizer.L("Clé API Google Gemini (stockée localement)", "Google Gemini API Key (stored locally)", "Clave API Google Gemini", "Google Gemini API-Schlüssel", "Chiave API Google Gemini", "Chave API Google Gemini");
        }

        private void SaveAiSettingsFromUi()
        {
            if (Localizer.CachedSettings == null)
                Localizer.CachedSettings = new AppSettings();
            if (_cbAiProvider != null && _cbAiProvider.SelectedIndex >= 0)
                Localizer.CachedSettings.AiProvider = _cbAiProvider.SelectedIndex;
            if (_pbAiApiKey != null)
                Localizer.CachedSettings.AiApiKey = _pbAiApiKey.Password ?? "";
            if (_cbClaudeModel != null && _cbClaudeModel.SelectedIndex >= 0)
                Localizer.CachedSettings.ClaudeModel = ClaudeAdvisor.Models[_cbClaudeModel.SelectedIndex][0];
            if (_txtOllamaModel != null && !string.IsNullOrWhiteSpace(_txtOllamaModel.Text))
                Localizer.CachedSettings.OllamaModel = _txtOllamaModel.Text.Trim();
            Localizer.SaveFullSettings(Localizer.CachedSettings);
        }

        private void OnCopyAiPromptClick()
        {
            try
            {
                Clipboard.SetText(AiAssistant.BuildStructuredPrompt(_lastResult, ""));
                SetToast(Localizer.L(
                    "Rapport IA copié : collez-le dans l'assistant de votre choix.",
                    "AI report copied: paste it into the assistant of your choice.",
                    "Informe IA copiado: péguelo en su asistente.",
                    "KI-Bericht kopiert: in einen Assistenten Ihrer Wahl einfügen.",
                    "Rapporto IA copiato: incollalo nell'assistente che preferisci.",
                    "Relatório IA copiado: cole no assistente de sua escolha."), "SuccessFg");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Clipboard");
            }
        }

        private void OnRegisterAntigravityClick()
        {
            string savedPath, err;
            bool ok = McpServer.RegisterInAntigravity(out savedPath, out err);
            RefreshMcpRegistrationStatus();
            if (ok)
                SetToast(Localizer.L(
                    "Serveur MCP Dump Finder activé dans Antigravity (" + savedPath + ").",
                    "Dump Finder MCP server enabled in Antigravity (" + savedPath + ").",
                    "Servidor MCP activado en Antigravity (" + savedPath + ").",
                    "Dump Finder MCP-Server in Antigravity aktiviert (" + savedPath + ").",
                    "Server MCP attivato in Antigravity (" + savedPath + ").",
                    "Servidor MCP ativado no Antigravity (" + savedPath + ")."), "SuccessFg");
            else
                SetToast("MCP : " + err, "DangerFg");
        }

        private void OnRegisterClaudeDesktopClick()
        {
            string path, err;
            bool ok = McpServer.RegisterInClaudeDesktop(out path, out err);
            RefreshMcpRegistrationStatus();
            SetToast(ok
                ? Localizer.L("Dump Finder ajouté à Claude Desktop. Redémarrez Claude Desktop pour l'activer.", "Dump Finder added to Claude Desktop. Restart Claude Desktop to enable it.", "Dump Finder añadido a Claude Desktop. Reinicie Claude Desktop.", "Dump Finder zu Claude Desktop hinzugefügt. Claude Desktop neu starten.", "Dump Finder aggiunto a Claude Desktop. Riavvia Claude Desktop.", "Dump Finder adicionado ao Claude Desktop. Reinicie o Claude Desktop.")
                : "MCP : " + err, ok ? "SuccessFg" : "DangerFg");
        }

        private void RefreshMcpRegistrationStatus()
        {
            if (_bdMcpStatusBadge == null || _txtMcpConfigPreview == null) return;
            bool claudeDesktop = McpServer.IsRegisteredInClaudeDesktop();
            bool registered = McpServer.IsRegisteredInAntigravity() || claudeDesktop;
            _bdMcpStatusBadge.SetResourceReference(Border.BackgroundProperty, registered ? "SuccessSoftBg" : "BgSubtle");
            _lblMcpStatusBadge.SetResourceReference(TextBlock.ForegroundProperty, registered ? "SuccessFg" : "TextSecondary");
            _lblMcpStatusBadge.Text = registered
                ? Localizer.L("● Connecté", "● Connected", "● Conectado", "● Verbunden", "● Connesso", "● Conectado") + (claudeDesktop ? " · Claude Desktop" : "") + (McpServer.IsRegisteredInAntigravity() ? " · Antigravity" : "")
                : Localizer.L("○ Prêt à activer", "○ Ready to enable", "○ Listo para activar", "○ Bereit zur Aktivierung", "○ Pronto da attivare", "○ Pronto para ativar");

            string cfgPath = McpServer.GetAntigravityConfigPath();
            string exeEscaped = McpServer.GetExePath().Replace("\\", "\\\\");
            _txtMcpConfigPreview.Text =
                "// Antigravity / Gemini : " + cfgPath + "\r\n" +
                "{ \"mcpServers\": { \"" + McpServer.ServerName + "\": { \"command\": \"" + exeEscaped + "\", \"args\": [\"--mcp\"] } } }\r\n" +
                "// Tools (7) : scan_system • check_hardware_and_conflicts • analyze_minidumps • get_crash_detail • list_fixes • apply_fix (dry run + on-screen approval) • export_report";
        }

        private void RefreshAiPageLabels()
        {
            if (_lblAiProviderCaption == null) return;
            _lblAiProviderCaption.Text = Localizer.L("Moteur IA", "AI Provider", "Proveedor IA", "KI-Anbieter", "Motore IA", "Provedor IA");
            UpdateAiCredentialVisibility();
            _btnCopyAiPrompt.Content = Localizer.L("📋 Copier le prompt", "📋 Copy prompt", "📋 Copiar prompt", "📋 Prompt kopieren", "📋 Copia prompt", "📋 Copiar prompt");

            _lblMcpCardTitle.Text = Localizer.L(
                "Serveur MCP (Antigravity · Claude Code · Gemini CLI)",
                "MCP server (Antigravity · Claude Code · Gemini CLI)",
                "Servidor MCP (Antigravity · Claude Code · Gemini CLI)",
                "MCP-Server (Antigravity · Claude Code · Gemini CLI)",
                "Server MCP (Antigravity · Claude Code · Gemini CLI)",
                "Servidor MCP (Antigravity · Claude Code · Gemini CLI)");
            _lblMcpCardDesc.Text = Localizer.L(
                "Dump Finder intègre un serveur Model Context Protocol. Une fois connecté, votre agent IA peut consulter vos crashs, vérifier votre matériel, décoder vos fichiers .dmp et appliquer un correctif avec votre accord.",
                "Dump Finder embeds a Model Context Protocol server. Once connected, your AI agent can query your crashes, audit your hardware, decode .dmp files and apply a fix with your approval.",
                "Dump Finder incluye un servidor MCP para que su agente IA consulte fallos, hardware y volcados .dmp.",
                "Dump Finder enthält einen MCP-Server, über den KI-Agenten Abstürze, Hardware und .dmp-Dateien abfragen können.",
                "Dump Finder include un server MCP: il tuo agente IA può consultare crash, hardware e file .dmp.",
                "O Dump Finder inclui um servidor MCP para que seu agente IA consulte falhas, hardware e arquivos .dmp.");
            _btnRegisterClaudeDesktop.Content = Localizer.L("Activer dans Claude Desktop", "Enable in Claude Desktop", "Activar en Claude Desktop", "In Claude Desktop aktivieren", "Attiva in Claude Desktop", "Ativar no Claude Desktop");
            _btnRegisterAntigravity.Content = Localizer.L("Activer dans Antigravity", "Enable in Antigravity", "Activar en Antigravity", "In Antigravity aktivieren", "Attiva in Antigravity", "Ativar no Antigravity");
            _btnCopyClaudeMcp.Content = Localizer.L("Copier la commande Claude Code", "Copy Claude Code command", "Copiar comando Claude Code", "Claude Code-Befehl kopieren", "Copia comando Claude Code", "Copiar comando Claude Code");
            _btnCopyGeminiMcp.Content = Localizer.L("Copier la commande Gemini CLI", "Copy Gemini CLI command", "Copiar comando Gemini CLI", "Gemini CLI-Befehl kopieren", "Copia comando Gemini CLI", "Copiar comando Gemini CLI");
            RefreshMcpRegistrationStatus();
        }
    }
}
