using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using NAudio.Wave;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Collections.Generic;

namespace AgendadorRadioVisual
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new JanelaPrincipal());
        }
    }

    public class JanelaPrincipal : Form
    {
        private const string STRING_CONEXAO = "Data Source=config_radio.db";

        // --- API DO WINDOWS (HOTKEYS) ---
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // --- COMPONENTES DA INTERFACE ---
        private MenuStrip menuSuperior = null!;
        private TextBox txtPasta = null!;
        private NumericUpDown txtMinutos = null!;
        private Button btnSelecionarPasta = null!;
        private Button btnIniciar = null!;
        private Button btnParar = null!;
        private ListBox lstLog = null!;
        private ComboBox cmbAudioDevices = null!;

        private Label lblCronometro = null!;
        private Label lblProximoAudio = null!;
        private Label lblStatusAutomacao = null!;
        private Label lblProximoHorarioDisparo = null!;
        private Label lblContadorAudios = null!; 

        private DateTimePicker dtpInicio = null!;
        private DateTimePicker dtpFim = null!;
        private CheckBox chkAutoHorario = null!;
        private Button btnAplicarHorario = null!;

        private Button[] btnDisparos = new Button[3];
        private Button[] btnConfigDisparos = new Button[3];
        private string[] caminhosDisparos = new string[3] { "", "", "" };
        private Keys[] teclasDisparos = new Keys[3] { Keys.None, Keys.None, Keys.None };

        private System.Windows.Forms.Timer relogioMestre = null!;

        // --- CONTROLE DE ÁUDIO E STATUS ---
        private WaveOutEvent? dispositivoSaidaAtual = null;
        private AudioFileReader? arquivoAudioAtual = null;
        private readonly object travaAudio = new object();

        private Random random = new Random();
        private List<string> listaArquivosFila = new List<string>();
        private int indiceFilaAtual = 0;
        private string proximoAudioCaminho = "";
        private int segundosRestantes = 0;
        private bool agendadorAtivoPeloHorario = false;
        private bool fecharMinimiza = false;
        private bool audioEstaTocando = false; 

        // Configuração para o Desligamento
        private bool forcarDesligamentoWindows = false;

        // Cores Dark Mode
        private readonly Color CorFundoJanela = Color.FromArgb(28, 28, 30);
        private readonly Color CorFundoCampos = Color.FromArgb(44, 44, 46);
        private readonly Color CorTextoClaro = Color.FromArgb(242, 242, 247);
        private readonly Color CorTextoEscuro = Color.FromArgb(209, 209, 214);
        private readonly Color CorBotaoSucesso = Color.FromArgb(52, 199, 89);
        private readonly Color CorBotaoPerigo = Color.FromArgb(255, 59, 48);
        private readonly Color CorBotaoNormal = Color.FromArgb(58, 58, 60);

        public JanelaPrincipal()
        {
            this.Text = "AudioScheduler v1.0.2 - by: @ataliasloami";
            this.Size = new Size(550, 810); 
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = CorFundoJanela;

            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AudioSchedulerCSharp.logo.ico") ?? 
                                    Assembly.GetExecutingAssembly().GetManifestResourceStream("AgendadorRadioVisual.logo.ico"))
                {
                    if (stream != null) this.Icon = new Icon(stream);
                }
            }
            catch { }

            CriarMenuSuperior();
            CriarComponentVisual();
            ListarDispositivosAudio();
            ConfigurarRelogioMestre();

            ConfigurarBancoSQLite();
            CarregarConfiguracoesSalvas();
        }

        private void CriarMenuSuperior()
        {
            menuSuperior = new MenuStrip();
            menuSuperior.BackColor = CorFundoCampos;
            menuSuperior.ForeColor = CorTextoClaro;
            menuSuperior.Renderer = new ToolStripProfessionalRenderer(new ColorTemaEscuroMenu(CorFundoCampos, CorFundoJanela, CorTextoClaro));

            ToolStripMenuItem menuConfig = new ToolStripMenuItem("Configurações");
            menuConfig.ForeColor = CorTextoClaro;

            ToolStripMenuItem itemTopo = new ToolStripMenuItem("Sempre no Topo");
            itemTopo.CheckOnClick = true;
            itemTopo.ForeColor = CorTextoClaro; 
            itemTopo.BackColor = CorFundoCampos;
            itemTopo.CheckedChanged += (s, e) => { this.TopMost = itemTopo.Checked; };

            ToolStripMenuItem itemMinimizar = new ToolStripMenuItem("Fechar apenas minimiza o programa");
            itemMinimizar.CheckOnClick = true;
            itemMinimizar.ForeColor = CorTextoClaro;
            itemMinimizar.BackColor = CorFundoCampos;
            itemMinimizar.CheckedChanged += (s, e) => { this.fecharMinimiza = itemMinimizar.Checked; };

            ToolStripMenuItem itemDesligar = new ToolStripMenuItem("Forçar desligamento do Windows ao encerrar");
            itemDesligar.CheckOnClick = true;
            itemDesligar.ForeColor = Color.OrangeRed;
            itemDesligar.BackColor = CorFundoCampos;
            itemDesligar.CheckedChanged += (s, e) => {
                this.forcarDesligamentoWindows = itemDesligar.Checked;
                SalvarConfiguracoesAtuais();
            };

            menuConfig.DropDownItems.Add(itemTopo);
            menuConfig.DropDownItems.Add(itemMinimizar);
            menuConfig.DropDownItems.Add(itemDesligar);
            menuSuperior.Items.Add(menuConfig);

            this.MainMenuStrip = menuSuperior;
            this.Controls.Add(menuSuperior);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Application.DoEvents();
            System.Threading.Thread.Sleep(500); 

            // FIX: Só liga automaticamente se houver pasta válida E o controle por horário estiver ativo no banco
            if (!string.IsNullOrEmpty(txtPasta.Text) && Directory.Exists(txtPasta.Text) && chkAutoHorario.Checked)
            {
                AdicionarLog("Configuração lida com sucesso! Ativando monitoramento automático...");
                BtnIniciar_Click(this, EventArgs.Empty);
            }
            else if (!string.IsNullOrEmpty(txtPasta.Text) && Directory.Exists(txtPasta.Text))
            {
                // Se a automação por horário estiver desligada, apenas mapeia e atualiza a quantidade na tela de prontidão
                AtualizarListaDeArquivos();
                AdicionarLog("Configurações carregadas em Modo Manual. Pronto para iniciar.");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            for (int i = 0; i < 3; i++) UnregisterHotKey(this.Handle, i);

            if (fecharMinimiza && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized; 
                AdicionarLog("Programa ocultado na barra de tarefas.");
                return;
            }

            PararAudioAtual(); 
            SalvarConfiguracoesAtuais();
            base.OnFormClosing(e);
        }

        private void ConfigurarBancoSQLite()
        {
            try
            {
                using (var conexao = new SqliteConnection(STRING_CONEXAO))
                {
                    conexao.Open();
                    string queryTabela = @"
                        CREATE TABLE IF NOT EXISTS Config (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Pasta TEXT, Minutos INTEGER, Dispositivo TEXT,
                            UsarHorario TEXT, HoraInicio TEXT, HoraFim TEXT,
                            SempreNoTopo TEXT, FecharMinimiza TEXT, ForcarDesligar TEXT,
                            Disp1Path TEXT, Disp1Key INTEGER,
                            Disp2Path TEXT, Disp2Key INTEGER,
                            Disp3Path TEXT, Disp3Key INTEGER
                        );";
                    using (var comando = new SqliteCommand(queryTabela, conexao)) { comando.ExecuteNonQuery(); }
                }
            }
            catch (Exception ex) { MessageBox.Show($"Erro no SQLite: {ex.Message}"); }
        }

        public void SalvarConfiguracoesAtuais()
        {
            try
            {
                using (var conexao = new SqliteConnection(STRING_CONEXAO))
                {
                    conexao.Open();
                    using (var cmdLimpar = new SqliteCommand("DELETE FROM Config;", conexao)) { cmdLimpar.ExecuteNonQuery(); }

                    string queryInserir = @"
                        INSERT INTO Config (Pasta, Minutos, Dispositivo, UsarHorario, HoraInicio, HoraFim, SempreNoTopo, FecharMinimiza, ForcarDesligar,
                                            Disp1Path, Disp1Key, Disp2Path, Disp2Key, Disp3Path, Disp3Key) 
                        VALUES (@pasta, @minutos, @dispositivo, @usarHorario, @horaInicio, @horaFim, @topo, @min, @desligar,
                                @d1p, @d1k, @d2p, @d2k, @d3p, @d3k);";

                    using (var cmdInserir = new SqliteCommand(queryInserir, conexao))
                    {
                        cmdInserir.Parameters.AddWithValue("@pasta", txtPasta.Text);
                        cmdInserir.Parameters.AddWithValue("@minutos", (int)txtMinutos.Value);
                        cmdInserir.Parameters.AddWithValue("@dispositivo", cmbAudioDevices.SelectedItem?.ToString() ?? "");
                        cmdInserir.Parameters.AddWithValue("@usarHorario", chkAutoHorario.Checked.ToString());
                        cmdInserir.Parameters.AddWithValue("@horaInicio", dtpInicio.Value.ToString("HH:mm"));
                        cmdInserir.Parameters.AddWithValue("@horaFim", dtpFim.Value.ToString("HH:mm"));
                        cmdInserir.Parameters.AddWithValue("@topo", this.TopMost.ToString());
                        cmdInserir.Parameters.AddWithValue("@min", fecharMinimiza.ToString());
                        cmdInserir.Parameters.AddWithValue("@desligar", forcarDesligamentoWindows.ToString());

                        cmdInserir.Parameters.AddWithValue("@d1p", caminhosDisparos[0]);
                        cmdInserir.Parameters.AddWithValue("@d1k", (int)teclasDisparos[0]);
                        cmdInserir.Parameters.AddWithValue("@d2p", caminhosDisparos[1]); 
                        cmdInserir.Parameters.AddWithValue("@d2k", (int)teclasDisparos[1]);
                        cmdInserir.Parameters.AddWithValue("@d3p", caminhosDisparos[2]);
                        cmdInserir.Parameters.AddWithValue("@d3k", (int)teclasDisparos[2]);
                        cmdInserir.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("Erro ao salvar: " + ex.Message); }
        }

        private void CarregarConfiguracoesSalvas()
        {
            try
            {
                using (var conexao = new SqliteConnection(STRING_CONEXAO))
                {
                    conexao.Open();
                    using (var comando = new SqliteCommand("SELECT * FROM Config LIMIT 1;", conexao))
                    using (var resultado = comando.ExecuteReader())
                    {
                        if (resultado.Read())
                        {
                            txtPasta.Text = resultado["Pasta"].ToString() ?? "";
                            
                            if (bool.TryParse(resultado["UsarHorario"].ToString(), out bool usarHorario)) chkAutoHorario.Checked = usarHorario;
                            
                            AtualizarListaDeArquivos();

                            if (int.TryParse(resultado["Minutos"].ToString(), out int minutes)) txtMinutos.Value = minutes;

                            string dispositivoSalvo = resultado["Dispositivo"].ToString() ?? "";
                            for (int i = 0; i < cmbAudioDevices.Items.Count; i++)
                            {
                                if (cmbAudioDevices.Items[i]?.ToString() == dispositivoSalvo) { cmbAudioDevices.SelectedIndex = i; break; }
                            }

                            if (DateTime.TryParseExact(resultado["HoraInicio"].ToString(), "HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime horaInicio)) dtpInicio.Value = DateTime.Today.Add(horaInicio.TimeOfDay);
                            if (DateTime.TryParseExact(resultado["HoraFim"].ToString(), "HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime horaFim)) dtpFim.Value = DateTime.Today.Add(horaFim.TimeOfDay);

                            if (bool.TryParse(resultado["SempreNoTopo"].ToString(), out bool topo))
                            {
                                this.TopMost = topo;
                                if (menuSuperior.Items[0] is ToolStripMenuItem itemMenu) ((ToolStripMenuItem)itemMenu.DropDownItems[0]).Checked = topo;
                            }

                            if (bool.TryParse(resultado["FecharMinimiza"].ToString(), out bool min))
                            {
                                this.fecharMinimiza = min;
                                if (menuSuperior.Items[0] is ToolStripMenuItem itemMenu) ((ToolStripMenuItem)itemMenu.DropDownItems[1]).Checked = min;
                            }

                            if (bool.TryParse(resultado["ForcarDesligar"].ToString(), out bool desligar))
                            {
                                this.forcarDesligamentoWindows = desligar;
                                if (menuSuperior.Items[0] is ToolStripMenuItem itemMenu) ((ToolStripMenuItem)itemMenu.DropDownItems[2]).Checked = desligar;
                            }

                            for (int i = 0; i < 3; i++)
                            {
                                caminhosDisparos[i] = resultado[$"Disp{i+1}Path"].ToString() ?? "";
                                if (int.TryParse(resultado[$"Disp{i+1}Key"].ToString(), out int keyVal))
                                {
                                    teclasDisparos[i] = (Keys)keyVal;
                                    if (teclasDisparos[i] != Keys.None) RegisterHotKey(this.Handle, i, 0, (int)teclasDisparos[i]);
                                }
                                AtualizarVisualBotaoDisparo(i);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { AdicionarLog($"Erro ao ler SQLite: {ex.Message}"); }
        }

        private void CriarComponentVisual()
        {
            int margemTopo = 35; 

            Label lblPasta = new Label() { Text = "Pasta dos Áudios:", Location = new Point(20, margemTopo + 15), Size = new Size(150, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            
            txtPasta = new TextBox() { Location = new Point(20, margemTopo + 35), Size = new Size(390, 25), ReadOnly = true, BackColor = CorFundoCampos, ForeColor = CorTextoClaro, BorderStyle = BorderStyle.FixedSingle };
            btnSelecionarPasta = new Button() { Text = "Buscar...", Location = new Point(420, margemTopo + 34), Size = new Size(90, 26), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoNormal, ForeColor = CorTextoClaro };
            btnSelecionarPasta.FlatAppearance.BorderColor = CorFundoCampos;
            btnSelecionarPasta.Click += SelecionarPasta_Click;

            Label lblDevice = new Label() { Text = "Dispositivo de Saída (Fones/Mesa):", Location = new Point(20, margemTopo + 75), Size = new Size(300, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            cmbAudioDevices = new ComboBox() { Location = new Point(20, margemTopo + 95), Size = new Size(490, 25), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CorFundoCampos, ForeColor = CorTextoClaro, FlatStyle = FlatStyle.Flat };

            Label lblMinutos = new Label() { Text = "Intervalo (Minutos):", Location = new Point(20, margemTopo + 135), Size = new Size(130, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            txtMinutos = new NumericUpDown() { Location = new Point(20, margemTopo + 155), Size = new Size(80, 25), Minimum = 1, Maximum = 60, Value = 5, BackColor = CorFundoCampos, ForeColor = CorTextoClaro, BorderStyle = BorderStyle.FixedSingle };

            GroupBox grpAutomacao = new GroupBox() { Text = " AUTOMAÇÃO POR HORÁRIO ", Location = new Point(20, margemTopo + 195), Size = new Size(490, 95), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            
            // FIX: Corrigido o texto do CheckBox para CorTextoClaro garantindo leitura perfeita no Dark Mode
            chkAutoHorario = new CheckBox() { Text = "Ativar controle automático de horário", Location = new Point(15, 22), Size = new Size(290, 20), ForeColor = CorTextoClaro };
            chkAutoHorario.CheckedChanged += ChkAutoHorario_CheckedChanged;

            Label lblInicio = new Label() { Text = "Iniciar às:", Location = new Point(15, 54), Size = new Size(65, 20), ForeColor = CorTextoEscuro };
            dtpInicio = new DateTimePicker() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Location = new Point(85, 51), Size = new Size(70, 23), BackColor = CorFundoCampos, ForeColor = CorTextoClaro };

            Label lblFim = new Label() { Text = "Parar às:", Location = new Point(165, 54), Size = new Size(60, 20), ForeColor = CorTextoEscuro };
            dtpFim = new DateTimePicker() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Location = new Point(230, 51), Size = new Size(70, 23), BackColor = CorFundoCampos, ForeColor = CorTextoClaro };

            btnAplicarHorario = new Button() { Text = "Aplicar Horários", Location = new Point(315, 49), Size = new Size(160, 26), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoNormal, ForeColor = Color.Gold, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            btnAplicarHorario.FlatAppearance.BorderColor = Color.Gold;
            btnAplicarHorario.Click += BtnAplicarHorario_Click;

            lblStatusAutomacao = new Label() { Text = "Modo Manual", Location = new Point(330, 24), Size = new Size(140, 20), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8, FontStyle.Italic), TextAlign = ContentAlignment.TopRight };
            grpAutomacao.Controls.AddRange(new Control[] { chkAutoHorario, lblInicio, dtpInicio, lblFim, dtpFim, btnAplicarHorario, lblStatusAutomacao });

            btnIniciar = new Button() { Text = "LIGAR AGENDADOR", Location = new Point(20, margemTopo + 305), Size = new Size(235, 32), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoSucesso, ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            btnIniciar.Click += BtnIniciar_Click;

            btnParar = new Button() { Text = "DESLIGAR", Location = new Point(275, margemTopo + 305), Size = new Size(235, 32), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoPerigo, ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Enabled = false };
            btnParar.Click += BtnParar_Click;

            // --- GRUPO: MONITORAMENTO EM TEMPO REAL ---
            GroupBox grpStatus = new GroupBox() { Text = " MONITORAMENTO EM TEMPO REAL ", Location = new Point(20, margemTopo + 350), Size = new Size(490, 120), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            Label lblProximoTitulo = new Label() { Text = "PRÓXIMO ÁUDIO NA AGULHA:", Location = new Point(15, 22), Size = new Size(180, 15), ForeColor = CorTextoEscuro, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            
            // FIX: Movido o contador de áudios da pasta para dentro do grupo de Monitoramento com destaque
            lblContadorAudios = new Label() { Text = "[ 0 áudios na pasta ]", Location = new Point(320, 21), Size = new Size(155, 15), ForeColor = Color.LightSkyBlue, Font = new Font("Segoe UI", 8, FontStyle.Bold | FontStyle.Italic), TextAlign = ContentAlignment.TopRight };

            lblProximoAudio = new Label() { Text = "Aguardando início...", Location = new Point(15, 40), Size = new Size(460, 20), ForeColor = Color.Gold, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            Label lblTempoTitulo = new Label() { Text = "TEMPO RESTANTE:", Location = new Point(15, 75), Size = new Size(120, 15), ForeColor = CorTextoEscuro, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            lblCronometro = new Label() { Text = "00:00", Location = new Point(135, 65), Size = new Size(100, 32), ForeColor = Color.Cyan, Font = new Font("Segoe UI", 18, FontStyle.Bold) };
            Label lblProximaHoraTitulo = new Label() { Text = "PRÓXIMO DISPARO ÀS:", Location = new Point(245, 75), Size = new Size(130, 15), ForeColor = CorTextoEscuro, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            lblProximoHorarioDisparo = new Label() { Text = "--:--:--", Location = new Point(380, 71), Size = new Size(95, 22), ForeColor = Color.LightGreen, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
            grpStatus.Controls.AddRange(new Control[] { lblProximoTitulo, lblContadorAudios, lblProximoAudio, lblTempoTitulo, lblCronometro, lblProximaHoraTitulo, lblProximoHorarioDisparo });

            Label lblLog = new Label() { Text = "Histórico de Execução:", Location = new Point(20, margemTopo + 480), Size = new Size(200, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            lstLog = new ListBox() { Location = new Point(20, margemTopo + 500), Size = new Size(490, 95), BackColor = CorFundoCampos, ForeColor = CorTextoClaro, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9) };

            GroupBox grpCartucheira = new GroupBox() { Text = " DISPARO IMEDIATO (TECLAS DE ATALHO) ", Location = new Point(20, margemTopo + 605), Size = new Size(490, 95), ForeColor = Color.LightSkyBlue, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            for (int i = 0; i < 3; i++)
            {
                int indexId = i;
                btnDisparos[i] = new Button() { Text = "Vazio", Location = new Point(15 + (i * 155), 25), Size = new Size(110, 55), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(72, 72, 74), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
                btnDisparos[i].Click += (s, e) => ExecutarDisparoRápidoPorBotao(indexId);

                btnConfigDisparos[i] = new Button() { Text = "⚙️", Location = new Point(125 + (i * 155), 25), Size = new Size(30, 55), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoNormal, ForeColor = CorTextoClaro };
                btnConfigDisparos[i].Click += (s, e) => ConfigurarBotaoDisparoRapido(indexId);
                grpCartucheira.Controls.AddRange(new Control[] { btnDisparos[i], btnConfigDisparos[i] });
            }

            Label lblCreditos = new Label() { Text = "by: @ataliasloami", Location = new Point(20, margemTopo + 705), Size = new Size(490, 20), ForeColor = Color.FromArgb(100, 100, 104), Font = new Font("Segoe UI", 8, FontStyle.Italic), TextAlign = ContentAlignment.MiddleCenter };

            this.Controls.AddRange(new Control[] { 
                lblPasta, txtPasta, btnSelecionarPasta, lblDevice, cmbAudioDevices,
                lblMinutos, txtMinutos, grpAutomacao, btnIniciar, btnParar, grpStatus, lblLog, lstLog, grpCartucheira, lblCreditos 
            });
        }

        private void ListarDispositivosAudio()
        {
            cmbAudioDevices.Items.Clear();
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var caps = WaveOut.GetCapabilities(i);
                cmbAudioDevices.Items.Add(new DeviceItem { Index = i, Name = caps.ProductName });
            }
            if (cmbAudioDevices.Items.Count > 0) cmbAudioDevices.SelectedIndex = 0;
        }

        private void ConfigurarRelogioMestre()
        {
            relogioMestre = new System.Windows.Forms.Timer();
            relogioMestre.Interval = 1000; 
            relogioMestre.Tick += RelogioMestre_Tick;
        }

        private void ChkAutoHorario_CheckedChanged(object? sender, EventArgs e)
        {
            if (chkAutoHorario.Checked) { lblStatusAutomacao.Text = "Monitorando..."; lblStatusAutomacao.ForeColor = Color.Orange; }
            else { lblStatusAutomacao.Text = "Modo Manual"; lblStatusAutomacao.ForeColor = Color.Gray; }
        }

        private void BtnAplicarHorario_Click(object? sender, EventArgs e)
        {
            SalvarConfiguracoesAtuais();
            DialogResult resultado = MessageBox.Show(this, "Para aplicar as mudanças nos horários da rádio, o programa precisa ser reiniciado.\n\nDeseja reiniciar agora?", "Reiniciar Sistema", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (resultado == DialogResult.Yes)
            {
                Application.Restart();
                Environment.Exit(0);
            }
        }

        private void ConfigurarBotaoDisparoRapido(int index)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Arquivos de Áudio (*.mp3;*.wav)|*.mp3;*.wav";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    caminhosDisparos[index] = ofd.FileName;
                    Form formKey = new Form() { Text = "Escolha a Tecla", Size = new Size(280, 130), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedToolWindow, BackColor = CorFundoJanela };
                    Label lblMsg = new Label() { Text = "Pressione qualquer tecla do teclado (ex: F1, F2, 1, A...):", Location = new Point(15, 15), Size = new Size(250, 35), ForeColor = CorTextoClaro };
                    formKey.Controls.Add(lblMsg);

                    formKey.KeyDown += (s, e) => {
                        UnregisterHotKey(this.Handle, index); 
                        teclasDisparos[index] = e.KeyCode;
                        RegisterHotKey(this.Handle, index, 0, (int)e.KeyCode); 
                        formKey.Close();
                    };
                    formKey.ShowDialog(this);
                    AtualizarVisualBotaoDisparo(index);
                    SalvarConfiguracoesAtuais();
                    AdicionarLog($"Botão {index + 1} configurado! [Tecla: {teclasDisparos[index]}]");
                }
            }
        }

        private void AtualizarVisualBotaoDisparo(int index)
        {
            if (!string.IsNullOrEmpty(caminhosDisparos[index]))
            {
                string nomeCompleto = Path.GetFileNameWithoutExtension(caminhosDisparos[index]);
                string corteNome = nomeCompleto.Length > 10 ? nomeCompleto.Substring(0, 10) + ".." : nomeCompleto;
                btnDisparos[index].Text = $"{corteNome}\n[{teclasDisparos[index]}]";
                btnDisparos[index].BackColor = Color.FromArgb(0, 122, 255); 
            }
            else { btnDisparos[index].Text = "Vazio"; btnDisparos[index].BackColor = Color.FromArgb(72, 72, 74); }
        }

        private void ExecutarDisparoRápidoPorBotao(int index)
        {
            if (string.IsNullOrEmpty(caminhosDisparos[index]) || !File.Exists(caminhosDisparos[index])) return;
            
            AdicionarLog($"[Disparo Imediato] Soltando Botão {index + 1}: {Path.GetFileName(caminhosDisparos[index])}");
            ReproduzirArquivoEspecifico(caminhosDisparos[index]);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() >= 0 && m.WParam.ToInt32() < 3)
                ExecutarDisparoRápidoPorBotao(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        private void MicrosoftDataSqliteFix() { }

        private void SelecionarPasta_Click(object? sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                if (fbd.ShowDialog(this) == DialogResult.OK) { txtPasta.Text = fbd.SelectedPath; AtualizarListaDeArquivos(); }
            }
        }

        private void AtualizarListaDeArquivos()
        {
            string[] formatosSuportados = { ".mp3", ".wav" };
            try
            {
                if (!string.IsNullOrEmpty(txtPasta.Text) && Directory.Exists(txtPasta.Text))
                {
                    var arquivosAtuais = Directory.GetFiles(txtPasta.Text)
                                                  .Where(file => formatosSuportados.Contains(Path.GetExtension(file).ToLower()))
                                                  .ToList();

                    lblContadorAudios.Text = $"[ {arquivosAtuais.Count} áudios na pasta ]";

                    if (listaArquivosFila.Count == 0 || listaArquivosFila.Count != arquivosAtuais.Count)
                    {
                        ReconstruirEEmbaralharFila(arquivosAtuais);
                    }
                }
                else
                {
                    listaArquivosFila.Clear();
                    lblContadorAudios.Text = "[ 0 áudios na pasta ]";
                }
            }
            catch 
            { 
                listaArquivosFila.Clear(); 
                lblContadorAudios.Text = "[ 0 áudios na pasta ]"; 
            }
        }

        private void ReconstruirEEmbaralharFila(List<string> arquivos)
        {
            listaArquivosFila = new List<string>(arquivos);
            
            int n = listaArquivosFila.Count;
            while (n > 1)
            {
                n--;
                int k = random.Next(n + 1);
                string value = listaArquivosFila[k];
                listaArquivosFila[k] = listaArquivosFila[n];
                listaArquivosFila[n] = value;
            }
            indiceFilaAtual = 0;
        }

        private void SortearProximoAudio()
        {
            if (listaArquivosFila.Count == 0) 
            { 
                lblProximoAudio.Text = "Buscando arquivos na pasta..."; 
                proximoAudioCaminho = ""; 
                return; 
            }

            if (indiceFilaAtual >= listaArquivosFila.Count)
            {
                string[] formatosSuportados = { ".mp3", ".wav" };
                var arquivosAtuais = Directory.GetFiles(txtPasta.Text)
                                              .Where(file => formatosSuportados.Contains(Path.GetExtension(file).ToLower()))
                                              .ToList();
                ReconstruirEEmbaralharFila(arquivosAtuais);
            }

            if (listaArquivosFila.Count > 0)
            {
                proximoAudioCaminho = listaArquivosFila[indiceFilaAtual];
                lblProximoAudio.Text = Path.GetFileName(proximoAudioCaminho);
            }
        }

        private void BtnIniciar_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtPasta.Text) || !Directory.Exists(txtPasta.Text)) return;

            btnSelecionarPasta.Enabled = false; txtMinutos.Enabled = false; cmbAudioDevices.Enabled = false; chkAutoHorario.Enabled = false;
            btnIniciar.Enabled = false; btnParar.Enabled = true;

            agendadorAtivoPeloHorario = false; 
            
            AtualizarListaDeArquivos();
            relogioMestre.Start();

            if (chkAutoHorario.Checked)
            {
                AdicionarLog("Modo Automático Ativo."); lblStatusAutomacao.Text = "Monitorando Ativo"; lblStatusAutomacao.ForeColor = Color.LimeGreen;
                VerificarJanelaDeHorario();
            }
            else
            {
                SortearProximoAudio(); ExecutarToqueDoAudioSorteado();
                SortearProximoAudio(); segundosRestantes = (int)txtMinutos.Value * 60;
                CalcularEExibirProximoHorario(); AtualizarTextoCronometro();
                AdicionarLog($"Agendador manual ligado! Ciclo: {txtMinutos.Value} min.");
            }
        }

        private void BtnParar_Click(object? sender, EventArgs e)
        {
            relogioMestre.Stop();
            btnSelecionarPasta.Enabled = true; txtMinutos.Enabled = true; cmbAudioDevices.Enabled = true; chkAutoHorario.Enabled = true;
            btnIniciar.Enabled = true; btnParar.Enabled = false;

            segundosRestantes = 0; agendadorAtivoPeloHorario = false; proximoAudioCaminho = "";
            lblCronometro.Text = "00:00"; lblProximoAudio.Text = "Agendador de áudio desligado."; lblProximoHorarioDisparo.Text = "--:--:--";
            lblStatusAutomacao.Text = chkAutoHorario.Checked ? "Monitorando..." : "Modo Manual";
            lblStatusAutomacao.ForeColor = chkAutoHorario.Checked ? Color.Orange : Color.Gray;

            PararAudioAtual();
            AdicionarLog("Agendador parado.");
        }

        private void RelogioMestre_Tick(object? sender, EventArgs e)
        {
            AtualizarListaDeArquivos();

            if (chkAutoHorario.Checked) VerificarJanelaDeHorario();

            if (!audioEstaTocando)
            {
                if (!chkAutoHorario.Checked || agendadorAtivoPeloHorario)
                {
                    if (segundosRestantes > 0)
                    {
                        segundosRestantes--; AtualizarTextoCronometro();
                        if (segundosRestantes == 0) ExecutarToqueDoAudioSorteado();
                    }
                }
            }
            else if (segundosRestantes > 0) CalcularEExibirProximoHorario();
        }

        private void ExecutarDesligamentoDoSistema()
        {
            try
            {
                AdicionarLog("⚠️ CRÍTICO: Horário limite atingido! Iniciando desligamento em 60 segundos...");
                Process.Start("shutdown", "/s /f /t 60");
            }
            catch (Exception ex)
            {
                AdicionarLog($"Erro ao acionar desligamento: {ex.Message}");
            }
        }

        private void VerificarJanelaDeHorario()
        {
            TimeSpan horaAtual = DateTime.Now.TimeOfDay;
            TimeSpan horaInicio = dtpInicio.Value.TimeOfDay;
            TimeSpan horaFim = dtpFim.Value.TimeOfDay;

            bool estaNaJanelaDeTempo = horaInicio <= horaFim ? (horaAtual >= horaInicio && horaAtual <= horaFim) : (horaAtual >= horaInicio || horaAtual <= horaFim);

            if (!estaNaJanelaDeTempo)
            {
                if (agendadorAtivoPeloHorario || lblProximoAudio.Text == "Aguardando início...")
                {
                    AdicionarLog("Horário de encerramento da automação atingido.");
                    agendadorAtivoPeloHorario = false; lblProximoAudio.Text = "Fora do horário de funcionamento.";
                    lblCronometro.Text = "--:--"; lblProximoHorarioDisparo.Text = "--:--:--"; segundosRestantes = 0; PararAudioAtual();

                    if (forcarDesligamentoWindows)
                    {
                        ExecutarDesligamentoDoSistema();
                    }
                }
            }
            else if (!agendadorAtivoPeloHorario)
            {
                AdicionarLog("Janela de horário válida atingida! Soltando primeiro disparo automático...");
                agendadorAtivoPeloHorario = true; 
                SortearProximoAudio(); 
                ExecutarToqueDoAudioSorteado();
            }
        }

        private void CalcularEExibirProximoHorario() => lblProximoHorarioDisparo.Text = DateTime.Now.AddSeconds(segundosRestantes).ToString("HH:mm:ss");
        private void AtualizarTextoCronometro() => lblCronometro.Text = TimeSpan.FromSeconds(segundosRestantes).ToString(@"mm\:ss");
        
        private void ExecutarToqueDoAudioSorteado() 
        { 
            if (string.IsNullOrEmpty(proximoAudioCaminho)) return;
            
            AdicionarLog($"[Agendador] Executando disparo automático: {Path.GetFileName(proximoAudioCaminho)}");
            ReproduzirArquivoEspecifico(proximoAudioCaminho, true); 
            
            indiceFilaAtual++;
        }

        private void PararAudioAtual()
        {
            lock (travaAudio)
            {
                if (dispositivoSaidaAtual != null) { try { dispositivoSaidaAtual.Stop(); dispositivoSaidaAtual.Dispose(); } catch { } dispositivoSaidaAtual = null; }
                if (arquivoAudioAtual != null) { try { arquivoAudioAtual.Dispose(); } catch { } arquivoAudioAtual = null; }
                audioEstaTocando = false;
            }
        }

        private void ReproduzirArquivoEspecifico(string caminhoDoSom, bool ehDoAgendador = false)
        {
            if (!File.Exists(caminhoDoSom)) return;
            PararAudioAtual();

            int dispositivoIdReal = 0; string textoSelecionadoNaTela = cmbAudioDevices.SelectedItem?.ToString() ?? "";
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var caps = WaveOut.GetCapabilities(i);
                if ($"[{i}] {caps.ProductName}" == textoSelecionadoNaTela) { dispositivoIdReal = i; break; }
            }

            System.Threading.Tasks.Task.Run(() => {
                try
                {
                    lock (travaAudio)
                    {
                        arquivoAudioAtual = new AudioFileReader(caminhoDoSom);
                        dispositivoSaidaAtual = new WaveOutEvent { DeviceNumber = dispositivoIdReal };
                        dispositivoSaidaAtual.Init(arquivoAudioAtual);
                        audioEstaTocando = true; dispositivoSaidaAtual.Play();
                    }

                    while (true)
                    {
                        lock (travaAudio) { if (dispositivoSaidaAtual == null || dispositivoSaidaAtual.PlaybackState != PlaybackState.Playing) break; }
                        System.Threading.Thread.Sleep(100);
                    }
                }
                catch (Exception ex) { this.BeginInvoke(new Action(() => AdicionarLog($"AVISO ÁUDIO: {ex.Message}"))); }
                finally
                {
                    lock (travaAudio) { audioEstaTocando = false; }
                    
                    this.BeginInvoke(new Action(() => {
                        if (ehDoAgendador)
                        {
                            SortearProximoAudio(); segundosRestantes = (int)txtMinutos.Value * 60;
                            CalcularEExibirProximoHorario(); AtualizarTextoCronometro();
                        }
                    }));
                }
            });
        }

        public void AdicionarLog(string message)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(() => AdicionarLog(message))); return; }
            lstLog.Items.Add($"[{DateTime.Now.ToString("HH:mm:ss")}] {message}");
            lstLog.TopIndex = lstLog.Items.Count - 1;
        }
    }

    public class ColorTemaEscuroMenu : ProfessionalColorTable
    {
        private Color fundoCampos; private Color fundoJanela; private Color textoClaro;
        public ColorTemaEscuroMenu(Color c, Color j, Color t) { fundoCampos = c; fundoJanela = j; textoClaro = t; }
        public override Color ToolStripDropDownBackground => fundoCampos;
        public override Color ImageMarginGradientBegin => fundoCampos;
        public override Color ImageMarginGradientMiddle => fundoCampos;
        public override Color ImageMarginGradientEnd => fundoCampos;
        public override Color MenuStripGradientBegin => fundoCampos;
        public override Color MenuStripGradientEnd => fundoCampos;
        public override Color MenuItemSelected => fundoJanela;
        public override Color MenuItemBorder => Color.Gray;
        public override Color MenuItemSelectedGradientBegin => fundoJanela;
        public override Color MenuItemSelectedGradientEnd => fundoJanela;
        public override Color MenuItemPressedGradientBegin => fundoCampos;
        public override Color MenuItemPressedGradientEnd => fundoCampos;
    }

    public class DeviceItem
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public override string ToString() => $"[{Index}] {Name}";
    }
}