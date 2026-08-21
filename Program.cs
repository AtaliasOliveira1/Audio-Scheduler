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
            // Prevenir múltiplas instâncias do programa
            bool createdNew;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, "AudioScheduler_v1.0.5_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("O AudioScheduler já está em execução! Verifique a barra de tarefas.", "AudioScheduler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new JanelaPrincipal());
            }
        }
    }

    public class JanelaPrincipal : Form
    {
        private string STRING_CONEXAO = null!;

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
        private ComboBox cmbCaptureDevices = null!;
        private ComboBox cmbSpotifyOutputDevice = null!;
        private TrackBar trkVolumeSpotify = null!;
        private TrackBar trkVolumeVoz = null!;
        private Label lblVolumeSpotify = null!;
        private Label lblVolumeVoz = null!;
        private TrackBar trkVolumeDucking = null!;
        private TrackBar trkVolumeDuckingBotao = null!;
        private TrackBar trkAtrasoDucking = null!;
        private Label lblVolumeDucking = null!;
        private Label lblVolumeDuckingBotao = null!;
        private Label lblAtrasoDucking = null!;

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
        private float[] volumesDisparos = new float[3] { 1.0f, 1.0f, 1.0f }; // Volume individual para cada botão
        private TrackBar[] trkVolumeDisparos = new TrackBar[3];
        private Label[] lblVolumeDisparos = new Label[3];

        private System.Windows.Forms.Timer relogioMestre = null!;

        // --- CONTROLE DE ÁUDIO E STATUS ---
        private WaveOutEvent? dispositivoSaidaAtual = null;
        private AudioFileReader? arquivoAudioAtual = null;
        private readonly object travaAudio = new object();

        // --- CONFIGURAÇÕES DO MIXER SPOTIFY (AUDIO DUCKING) ---
        private float volumeMinimoSpotify = 0.2f; // 20% de volume no ducking (configurável pelo usuário)
        private float volumeMinimoSpotifyBotao = 0.2f; // Volume do ducking para sons do botão (configurável)
        private int tempoTransicaoMs = 500; // Tempo de transição do fade (ms)
        private int atrasoDuckingMs = 0; // Atraso antes de iniciar o ducking (ms)

        // --- COMPONENTES DO MIXER SPOTIFY ---
        private WaveInEvent? waveInSpotify = null;
        private BufferedWaveProvider? bufferedWaveProviderSpotify = null;
        private WaveOutEvent? waveOutSpotify = null;
        private FadeSampleProvider? fadeSampleProvider = null;
        private float volumeAtualSpotify = 1.0f;
        private float volumeUsuarioSpotify = 1.0f; // Volume controlado pelo usuário
        private float volumeUsuarioVoz = 1.0f; // Volume controlado pelo usuário
        private readonly object travaSpotify = new object();
        private bool spotifyCapturaAtiva = false;

        private Random random = new Random();
        private List<string> listaArquivosFila = new List<string>();
        private int indiceFilaAtual = 0;
        private string proximoAudioCaminho = "";
        private int segundosRestantes = 0;
        private bool agendadorAtivoPeloHorario = false;
        private bool fecharMinimiza = false;
        private bool audioEstaTocando = false;
        private int? indiceBotaoTocando = null; // Rastreia qual botão está tocando atualmente 

        // Configuração para o Desligamento
        private bool forcarDesligamentoWindows = false;

        // Cores Dark Mode Moderno
        private readonly Color CorFundoJanela = Color.FromArgb(30, 30, 35);
        private readonly Color CorFundoCampos = Color.FromArgb(45, 45, 50);
        private readonly Color CorFundoGroupBox = Color.FromArgb(38, 38, 42);
        private readonly Color CorTextoClaro = Color.FromArgb(250, 250, 255);
        private readonly Color CorTextoEscuro = Color.FromArgb(200, 200, 210);
        private readonly Color CorBotaoSucesso = Color.FromArgb(46, 204, 113);
        private readonly Color CorBotaoPerigo = Color.FromArgb(231, 76, 60);
        private readonly Color CorBotaoNormal = Color.FromArgb(52, 152, 219);
        private readonly Color CorBordaSuave = Color.FromArgb(60, 60, 70);

        public JanelaPrincipal()
        {
            // Configurar caminho do banco de dados para pasta AppData do usuário (tem permissão de escrita)
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configFolder = Path.Combine(appDataPath, "AudioScheduler");
            
            // Criar pasta se não existir
            if (!Directory.Exists(configFolder))
            {
                Directory.CreateDirectory(configFolder);
            }
            
            string dbPath = Path.Combine(configFolder, "config_radio.db");
            STRING_CONEXAO = $"Data Source={dbPath}";

            this.Text = "AudioScheduler v1.0.5 - by: @ataliasloami";
            this.Size = new Size(1200, 750); 
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            //this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(1200, 750);
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
            ConfigurarBancoSQLite();
            ListarDispositivosAudio();
            ListarDispositivosCaptura();
            ListarDispositivosSaidaAudioExterno();
            ConfigurarControlesVolume();
            CarregarConfiguracoesSalvas();
            ConfigurarRelogioMestre();

            // Iniciar captura do áudio automaticamente ao abrir o programa (após carregar configurações)
            IniciarCapturaSpotify();
        }

        private void CriarMenuSuperior()
        {
            menuSuperior = new MenuStrip();
            menuSuperior.BackColor = CorFundoCampos;
            menuSuperior.ForeColor = CorTextoClaro;
            menuSuperior.Renderer = new ToolStripProfessionalRenderer(new ColorTemaEscuroMenu(CorFundoCampos, CorFundoJanela, CorTextoClaro));

            ToolStripMenuItem menuArquivo = new ToolStripMenuItem("Arquivo");
            menuArquivo.ForeColor = CorTextoClaro;

            ToolStripMenuItem itemSalvar = new ToolStripMenuItem("Salvar Configurações");
            itemSalvar.ForeColor = CorTextoClaro;
            itemSalvar.BackColor = CorFundoCampos;
            itemSalvar.ShortcutKeys = Keys.Control | Keys.S;
            itemSalvar.Click += (s, e) => {
                SalvarConfiguracoesAtuais();
            };

            ToolStripMenuItem itemSair = new ToolStripMenuItem("Sair");
            itemSair.ForeColor = CorTextoClaro;
            itemSair.BackColor = CorFundoCampos;
            itemSair.Click += (s, e) => {
                this.Close();
            };

            menuArquivo.DropDownItems.Add(itemSalvar);
            menuArquivo.DropDownItems.Add(new ToolStripSeparator());
            menuArquivo.DropDownItems.Add(itemSair);
            menuSuperior.Items.Add(menuArquivo);

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
            };

            menuConfig.DropDownItems.Add(itemTopo);
            menuConfig.DropDownItems.Add(itemMinimizar);
            menuConfig.DropDownItems.Add(itemDesligar);
            menuSuperior.Items.Add(menuConfig);

            ToolStripMenuItem menuSobre = new ToolStripMenuItem("Sobre");
            menuSobre.ForeColor = CorTextoClaro;
            ToolStripMenuItem itemInfo = new ToolStripMenuItem("Informações do Desenvolvedor");
            itemInfo.ForeColor = CorTextoClaro;
            itemInfo.BackColor = CorFundoCampos;
            itemInfo.Click += (s, e) => MostrarSobre();
            menuSobre.DropDownItems.Add(itemInfo);
            menuSuperior.Items.Add(menuSobre);

            this.MainMenuStrip = menuSuperior;
            this.Controls.Add(menuSuperior);
        }

        private void MostrarSobre()
        {
            string mensagem = "AudioScheduler v1.0.5\n\nDesenvolvido por: Atalias Lô-Amí\n\n" +
                           "📱 WhatsApp: (99) 98469-1168\n" +
                           "📷 Instagram: @ataliasloami_\n" +
                           "💬 Discord: ataliasloami\n" +
                           "📧 Email: ataliasoliveira37@gmail.com";
            
            MessageBox.Show(this, mensagem, "Sobre - AudioScheduler", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                // Salvar configurações mesmo ao minimizar
                SalvarConfiguracoesAtuais();
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
                            Disp1Path TEXT, Disp1Key INTEGER, Disp1Volume REAL,
                            Disp2Path TEXT, Disp2Key INTEGER, Disp2Volume REAL,
                            Disp3Path TEXT, Disp3Key INTEGER, Disp3Volume REAL,
                            DispositivoCaptura TEXT,
                            DispositivoSaidaSpotify TEXT,
                            VolumeSpotify REAL,
                            VolumeVoz REAL,
                            VolumeDucking REAL,
                            VolumeDuckingBotao REAL,
                            AtrasoDucking INTEGER
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
                                            Disp1Path, Disp1Key, Disp1Volume, Disp2Path, Disp2Key, Disp2Volume, Disp3Path, Disp3Key, Disp3Volume, DispositivoCaptura, DispositivoSaidaSpotify, VolumeSpotify, VolumeVoz, VolumeDucking, VolumeDuckingBotao, AtrasoDucking) 
                        VALUES (@pasta, @minutos, @dispositivo, @usarHorario, @horaInicio, @horaFim, @topo, @min, @desligar,
                                @d1p, @d1k, @d1v, @d2p, @d2k, @d2v, @d3p, @d3k, @d3v, @dispCaptura, @dispSaidaSpotify, @volSpotify, @volVoz, @volDucking, @volDuckingBotao, @atrasoDucking);";

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
                        cmdInserir.Parameters.AddWithValue("@d1v", volumesDisparos[0]);
                        cmdInserir.Parameters.AddWithValue("@d2p", caminhosDisparos[1]); 
                        cmdInserir.Parameters.AddWithValue("@d2k", (int)teclasDisparos[1]);
                        cmdInserir.Parameters.AddWithValue("@d2v", volumesDisparos[1]);
                        cmdInserir.Parameters.AddWithValue("@d3p", caminhosDisparos[2]);
                        cmdInserir.Parameters.AddWithValue("@d3k", (int)teclasDisparos[2]);
                        cmdInserir.Parameters.AddWithValue("@d3v", volumesDisparos[2]);
                        cmdInserir.Parameters.AddWithValue("@dispCaptura", cmbCaptureDevices.SelectedItem?.ToString() ?? "");
                        cmdInserir.Parameters.AddWithValue("@dispSaidaSpotify", cmbSpotifyOutputDevice.SelectedItem?.ToString() ?? "");
                        cmdInserir.Parameters.AddWithValue("@volSpotify", volumeUsuarioSpotify);
                        cmdInserir.Parameters.AddWithValue("@volVoz", volumeUsuarioVoz);
                        cmdInserir.Parameters.AddWithValue("@volDucking", volumeMinimoSpotify);
                        cmdInserir.Parameters.AddWithValue("@volDuckingBotao", volumeMinimoSpotifyBotao);
                        cmdInserir.Parameters.AddWithValue("@atrasoDucking", atrasoDuckingMs);
                        cmdInserir.ExecuteNonQuery();
                    }
                }
                AdicionarLog("Configurações salvas com sucesso no banco de dados.");
            }
            catch (Exception ex) 
            { 
                AdicionarLog($"ERRO AO SALVAR CONFIGURAÇÕES: {ex.Message}"); 
                MessageBox.Show($"Erro ao salvar configurações: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
                            var pasta = resultado["Pasta"];
                            txtPasta.Text = pasta != DBNull.Value ? pasta.ToString() ?? "" : "";
                            
                            var usarHorario = resultado["UsarHorario"];
                            if (usarHorario != DBNull.Value && bool.TryParse(usarHorario.ToString(), out bool usarHorarioBool)) chkAutoHorario.Checked = usarHorarioBool;
                            
                            AtualizarListaDeArquivos();

                            var minutos = resultado["Minutos"];
                            if (minutos != DBNull.Value && int.TryParse(minutos.ToString(), out int minutes)) txtMinutos.Value = minutes;

                            var dispositivo = resultado["Dispositivo"];
                            string dispositivoSalvo = dispositivo != DBNull.Value ? dispositivo.ToString() ?? "" : "";
                            for (int i = 0; i < cmbAudioDevices.Items.Count; i++)
                            {
                                if (cmbAudioDevices.Items[i]?.ToString() == dispositivoSalvo) { cmbAudioDevices.SelectedIndex = i; break; }
                            }

                            var horaInicio = resultado["HoraInicio"];
                            if (horaInicio != DBNull.Value && DateTime.TryParseExact(horaInicio.ToString(), "HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime horaInicioDt)) dtpInicio.Value = DateTime.Today.Add(horaInicioDt.TimeOfDay);
                            
                            var horaFim = resultado["HoraFim"];
                            if (horaFim != DBNull.Value && DateTime.TryParseExact(horaFim.ToString(), "HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime horaFimDt)) dtpFim.Value = DateTime.Today.Add(horaFimDt.TimeOfDay);

                            var topo = resultado["SempreNoTopo"];
                            if (topo != DBNull.Value && bool.TryParse(topo.ToString(), out bool topoBool))
                            {
                                this.TopMost = topoBool;
                                if (menuSuperior.Items[1] is ToolStripMenuItem itemMenu) ((ToolStripMenuItem)itemMenu.DropDownItems[0]).Checked = topoBool;
                            }

                            var min = resultado["FecharMinimiza"];
                            if (min != DBNull.Value && bool.TryParse(min.ToString(), out bool minBool))
                            {
                                this.fecharMinimiza = minBool;
                                if (menuSuperior.Items[1] is ToolStripMenuItem itemMenu) ((ToolStripMenuItem)itemMenu.DropDownItems[1]).Checked = minBool;
                            }

                            var desligar = resultado["ForcarDesligar"];
                            if (desligar != DBNull.Value && bool.TryParse(desligar.ToString(), out bool desligarBool))
                            {
                                this.forcarDesligamentoWindows = desligarBool;
                                if (menuSuperior.Items[1] is ToolStripMenuItem itemMenu) ((ToolStripMenuItem)itemMenu.DropDownItems[2]).Checked = desligarBool;
                            }

                            var dispCaptura = resultado["DispositivoCaptura"];
                            string dispositivoCapturaSalvo = dispCaptura != DBNull.Value ? dispCaptura.ToString() ?? "" : "";
                            for (int i = 0; i < cmbCaptureDevices.Items.Count; i++)
                            {
                                if (cmbCaptureDevices.Items[i]?.ToString() == dispositivoCapturaSalvo) { cmbCaptureDevices.SelectedIndex = i; break; }
                            }

                            var dispSaidaSpotify = resultado["DispositivoSaidaSpotify"];
                            string dispositivoSaidaSpotifySalvo = dispSaidaSpotify != DBNull.Value ? dispSaidaSpotify.ToString() ?? "" : "";
                            for (int i = 0; i < cmbSpotifyOutputDevice.Items.Count; i++)
                            {
                                if (cmbSpotifyOutputDevice.Items[i]?.ToString() == dispositivoSaidaSpotifySalvo) { cmbSpotifyOutputDevice.SelectedIndex = i; break; }
                            }

                            var volSpotify = resultado["VolumeSpotify"];
                            if (volSpotify != DBNull.Value && float.TryParse(volSpotify.ToString(), out float volSpotifyFloat))
                            {
                                volumeUsuarioSpotify = volSpotifyFloat;
                                trkVolumeSpotify.Value = (int)(volSpotifyFloat * 100);
                                lblVolumeSpotify.Text = $"{trkVolumeSpotify.Value}%";
                            }

                            var volVoz = resultado["VolumeVoz"];
                            if (volVoz != DBNull.Value && float.TryParse(volVoz.ToString(), out float volVozFloat))
                            {
                                volumeUsuarioVoz = volVozFloat;
                                trkVolumeVoz.Value = (int)(volVozFloat * 100);
                                lblVolumeVoz.Text = $"{trkVolumeVoz.Value}%";
                            }

                            var volDucking = resultado["VolumeDucking"];
                            if (volDucking != DBNull.Value && float.TryParse(volDucking.ToString(), out float volDuckingFloat))
                            {
                                volumeMinimoSpotify = volDuckingFloat;
                                trkVolumeDucking.Value = (int)(volDuckingFloat * 100);
                                lblVolumeDucking.Text = $"{trkVolumeDucking.Value}%";
                            }

                            var volDuckingBotao = resultado["VolumeDuckingBotao"];
                            if (volDuckingBotao != DBNull.Value && float.TryParse(volDuckingBotao.ToString(), out float volDuckingBotaoFloat))
                            {
                                volumeMinimoSpotifyBotao = volDuckingBotaoFloat;
                                trkVolumeDuckingBotao.Value = (int)(volDuckingBotaoFloat * 100);
                                lblVolumeDuckingBotao.Text = $"{trkVolumeDuckingBotao.Value}%";
                            }

                            var atrasoDucking = resultado["AtrasoDucking"];
                            if (atrasoDucking != DBNull.Value && int.TryParse(atrasoDucking.ToString(), out int atrasoDuckingInt))
                            {
                                atrasoDuckingMs = atrasoDuckingInt;
                                trkAtrasoDucking.Value = atrasoDuckingInt;
                                lblAtrasoDucking.Text = $"{trkAtrasoDucking.Value}ms";
                            }

                            for (int i = 0; i < 3; i++)
                            {
                                var dispPath = resultado[$"Disp{i+1}Path"];
                                caminhosDisparos[i] = dispPath != DBNull.Value ? dispPath.ToString() ?? "" : "";
                                
                                var dispKey = resultado[$"Disp{i+1}Key"];
                                if (dispKey != DBNull.Value && int.TryParse(dispKey.ToString(), out int keyVal))
                                {
                                    teclasDisparos[i] = (Keys)keyVal;
                                    if (teclasDisparos[i] != Keys.None) RegisterHotKey(this.Handle, i, 0, (int)teclasDisparos[i]);
                                }
                                
                                var dispVol = resultado[$"Disp{i+1}Volume"];
                                if (dispVol != DBNull.Value && float.TryParse(dispVol.ToString(), out float volDisp))
                                {
                                    volumesDisparos[i] = volDisp;
                                    if (trkVolumeDisparos[i] != null)
                                    {
                                        trkVolumeDisparos[i].Value = (int)(volDisp * 100);
                                        lblVolumeDisparos[i].Text = $"{trkVolumeDisparos[i].Value}%";
                                    }
                                }
                                AtualizarVisualBotaoDisparo(i);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { AdicionarLog($"Erro ao ler SQLite: {ex.Message}\nStack: {ex.StackTrace}"); }
        }

private void CriarComponentVisual()
{
    int margemTopo = 45; // Mantém o topo afastado do MenuStrip
    int margemEsquerda = 20;
    int larguraPainelEsquerdo = 280;
    int espacoEntrePaineis = 20;
    int espacoEntreSecoes = 15;

    // O painel da direita começa onde o da esquerda termina + o espaço
    int margemPainelPrincipal = margemEsquerda + larguraPainelEsquerdo + espacoEntrePaineis;
    
    // Calculamos a largura restante dinamicamente para não estourar os 1200 da janela
    int larguraPainelPrincipal = this.ClientSize.Width - margemPainelPrincipal - margemEsquerda;

    // --- PAINEL ESQUERDO: GATILHO IMEDIATO ---
    GroupBox grpGatilho = new GroupBox() { Text = " ⚡ GATILHO IMEDIATO ", Location = new Point(margemEsquerda, margemTopo), Size = new Size(larguraPainelEsquerdo, 190), ForeColor = Color.LightSkyBlue, Font = new Font("Segoe UI", 10, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Left, BackColor = CorFundoGroupBox };
    for (int i = 0; i < 3; i++)
    {
        int indexId = i;
        btnDisparos[i] = new Button() { Text = "Vazio", Location = new Point(15, 30 + (i * 50)), Size = new Size(195, 40), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(72, 72, 74), ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        btnDisparos[i].Click += (s, e) => ExecutarDisparoRápidoPorBotao(indexId);

        btnConfigDisparos[i] = new Button() { Text = "⚙️", Location = new Point(220, 30 + (i * 50)), Size = new Size(45, 40), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoNormal, ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 12) };
        btnConfigDisparos[i].Click += (s, e) => ConfigurarBotaoDisparoRapido(indexId);
        grpGatilho.Controls.AddRange(new Control[] { btnDisparos[i], btnConfigDisparos[i] });
    }

    // --- PAINEL ESQUERDO: VOLUMES DOS BOTÕES ---
    GroupBox grpVolumesBotoes = new GroupBox() { Text = " 🔊 VOLUMES DOS BOTÕES ", Location = new Point(margemEsquerda, margemTopo + 195), Size = new Size(larguraPainelEsquerdo, 140), ForeColor = Color.LightGreen, Font = new Font("Segoe UI", 10, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Left, BackColor = CorFundoGroupBox };
    for (int i = 0; i < 3; i++)
    {
        int indexId = i;
        Label lblTituloVolBotao = new Label() { Text = $"BT {i + 1}", Location = new Point(15 + (i * 85), 25), Size = new Size(80, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
        trkVolumeDisparos[i] = new TrackBar() { Location = new Point(15 + (i * 85), 45), Size = new Size(80, 45), Minimum = 0, Maximum = 100, Value = (int)(volumesDisparos[i] * 100), TickFrequency = 10, BackColor = CorFundoCampos };
        lblVolumeDisparos[i] = new Label() { Text = $"{trkVolumeDisparos[i].Value}%", Location = new Point(15 + (i * 85), 95), Size = new Size(80, 20), ForeColor = Color.Cyan, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
        trkVolumeDisparos[i].Scroll += (s, e) => { volumesDisparos[indexId] = trkVolumeDisparos[indexId].Value / 100f; lblVolumeDisparos[indexId].Text = $"{trkVolumeDisparos[indexId].Value}%"; AtualizarVolumeDisparo(indexId); };
        grpVolumesBotoes.Controls.AddRange(new Control[] { lblTituloVolBotao, trkVolumeDisparos[i], lblVolumeDisparos[i] });
    }

    // --- PAINEL ESQUERDO: CONTROLE DE VOLUME (CORRIGIDO PARA CABER NA TELA) ---
    // Subimos o início para 'margemTopo + 335' e reduzimos a altura total para 325 (termina perfeitamente em Y=705)
    GroupBox grpVolume = new GroupBox() { Text = " 🎚️ CONTROLE DE VOLUME ", Location = new Point(margemEsquerda, margemTopo + 335), Size = new Size(larguraPainelEsquerdo, 325), ForeColor = Color.LightGreen, Font = new Font("Segoe UI", 10, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom, BackColor = CorFundoGroupBox };

    int col1 = 12, col2 = 77, col3 = 142, col4 = 207; 
    int hLabel1 = 25, hTrack1 = 50, hSubVol1 = 155;   // Descemos os textos das % verticais de 160 para 155 para ganhar espaço

    // CORRIGIDO: Posições verticais do atraso recalculadas para o novo tamanho de 325
    int hLabel2 = 180, hTrack2 = 205, hSubVol2 = 280; 

    // Coluna 1: Áudio Externo
    Label lblTituloSpotify = new Label() { Text = "EXTERNO", Location = new Point(col1, hLabel1), Size = new Size(60, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 8, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    trkVolumeSpotify = new TrackBar() { Location = new Point(col1, hTrack1), Size = new Size(50, 100), Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 10, BackColor = CorFundoCampos, Orientation = Orientation.Vertical };
    lblVolumeSpotify = new Label() { Text = "100%", Location = new Point(col1, hSubVol1), Size = new Size(50, 20), ForeColor = Color.Cyan, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    trkVolumeSpotify.Scroll += (s, e) => { volumeUsuarioSpotify = trkVolumeSpotify.Value / 100f; lblVolumeSpotify.Text = $"{trkVolumeSpotify.Value}%"; AtualizarVolumeSpotify(); };

    // Coluna 2: Voz
    Label lblTituloVoz = new Label() { Text = "VOZ", Location = new Point(col2, hLabel1), Size = new Size(50, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 8, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    trkVolumeVoz = new TrackBar() { Location = new Point(col2, hTrack1), Size = new Size(50, 100), Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 10, BackColor = CorFundoCampos, Orientation = Orientation.Vertical };
    lblVolumeVoz = new Label() { Text = "100%", Location = new Point(col2, hSubVol1), Size = new Size(50, 20), ForeColor = Color.Cyan, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    trkVolumeVoz.Scroll += (s, e) => { volumeUsuarioVoz = trkVolumeVoz.Value / 100f; lblVolumeVoz.Text = $"{trkVolumeVoz.Value}%"; AtualizarVolumeVoz(); };

    // Coluna 3: Ducking Voz
    Label lblTituloDucking = new Label() { Text = "DUCK VOZ", Location = new Point(col3, hLabel1), Size = new Size(60, 20), ForeColor = Color.Orange, Font = new Font("Segoe UI", 7, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    trkVolumeDucking = new TrackBar() { Location = new Point(col3, hTrack1), Size = new Size(50, 100), Minimum = 0, Maximum = 50, Value = 20, TickFrequency = 5, BackColor = CorFundoCampos, Orientation = Orientation.Vertical };
    lblVolumeDucking = new Label() { Text = "20%", Location = new Point(col3, hSubVol1), Size = new Size(50, 20), ForeColor = Color.Orange, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    trkVolumeDucking.Scroll += (s, e) => { volumeMinimoSpotify = trkVolumeDucking.Value / 100f; lblVolumeDucking.Text = $"{trkVolumeDucking.Value}%"; };

    // Coluna 4: Ducking Botão
    Label lblTituloDuckingBotao = new Label() { Text = "DUCK BTN", Location = new Point(col4, hLabel1), Size = new Size(60, 20), ForeColor = Color.Gold, Font = new Font("Segoe UI", 7, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    trkVolumeDuckingBotao = new TrackBar() { Location = new Point(col4, hTrack1), Size = new Size(50, 100), Minimum = 0, Maximum = 50, Value = 20, TickFrequency = 5, BackColor = CorFundoCampos, Orientation = Orientation.Vertical };
    lblVolumeDuckingBotao = new Label() { Text = "20%", Location = new Point(col4, hSubVol1), Size = new Size(50, 20), ForeColor = Color.Gold, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    trkVolumeDuckingBotao.Scroll += (s, e) => { volumeMinimoSpotifyBotao = trkVolumeDuckingBotao.Value / 100f; lblVolumeDuckingBotao.Text = $"{trkVolumeDuckingBotao.Value}%"; };

    // Seção de Atraso (Subida proporcionalmente para acompanhar o novo fundo menor)
    Label lblTituloAtraso = new Label() { Text = "ATRASO DUCKING (ms):", Location = new Point(15, hLabel2), Size = new Size(180, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 8, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
    trkAtrasoDucking = new TrackBar() { Location = new Point(12, hTrack2), Size = new Size(250, 40), Minimum = 0, Maximum = 1000, Value = 0, TickFrequency = 100, BackColor = CorFundoCampos, Orientation = Orientation.Horizontal };
    lblAtrasoDucking = new Label() { Text = "0ms", Location = new Point(12, hSubVol2), Size = new Size(250, 20), ForeColor = Color.Cyan, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    trkAtrasoDucking.Scroll += (s, e) => { atrasoDuckingMs = trkAtrasoDucking.Value; lblAtrasoDucking.Text = $"{trkAtrasoDucking.Value}ms"; };

    grpVolume.Controls.AddRange(new Control[] { 
        lblTituloSpotify, trkVolumeSpotify, lblVolumeSpotify, 
        lblTituloVoz, trkVolumeVoz, lblVolumeVoz, 
        lblTituloDucking, trkVolumeDucking, lblVolumeDucking, 
        lblTituloDuckingBotao, trkVolumeDuckingBotao, lblVolumeDuckingBotao, 
        lblTituloAtraso, trkAtrasoDucking, lblAtrasoDucking 
    });

    // --- SEÇÃO 1: CONFIGURAÇÕES DE ÁUDIO E CAMINHO ---
    GroupBox grpConfiguracoes = new GroupBox() { Text = " 📁 CONFIGURAÇÕES ", Location = new Point(margemPainelPrincipal, margemTopo), Size = new Size(larguraPainelPrincipal, 145), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 10, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = CorFundoGroupBox };
    
    Label lblPasta = new Label() { Text = "Pasta dos Áudios:", Location = new Point(15, 25), Size = new Size(120, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    txtPasta = new TextBox() { Location = new Point(15, 45), Size = new Size(larguraPainelPrincipal - 250, 25), ReadOnly = true, BackColor = CorFundoCampos, ForeColor = CorTextoClaro, BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    btnSelecionarPasta = new Button() { Text = "📂 Buscar", Location = new Point(larguraPainelPrincipal - 225, 44), Size = new Size(90, 26), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoNormal, ForeColor = CorTextoClaro, Anchor = AnchorStyles.Top | AnchorStyles.Right };
    btnSelecionarPasta.FlatAppearance.BorderColor = CorFundoCampos;
    btnSelecionarPasta.Click += SelecionarPasta_Click;

    Label lblMinutos = new Label() { Text = "Intervalo (Min):", Location = new Point(larguraPainelPrincipal - 120, 25), Size = new Size(100, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Right };
    txtMinutos = new NumericUpDown() { Location = new Point(larguraPainelPrincipal - 120, 45), Size = new Size(105, 25), Minimum = 1, Maximum = 60, Value = 5, BackColor = CorFundoCampos, ForeColor = CorTextoClaro, BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Top | AnchorStyles.Right };

    int larguraCombo = (larguraPainelPrincipal - 40) / 3;

    Label lblDevice = new Label() { Text = "Dispositivo Voz:", Location = new Point(15, 85), Size = new Size(150, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    cmbAudioDevices = new ComboBox() { Location = new Point(15, 105), Size = new Size(larguraCombo, 25), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CorFundoCampos, ForeColor = CorTextoClaro, FlatStyle = FlatStyle.Flat };

    Label lblCaptureDevice = new Label() { Text = "Dispositivo Captura:", Location = new Point(15 + larguraCombo + 10, 85), Size = new Size(150, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    cmbCaptureDevices = new ComboBox() { Location = new Point(15 + larguraCombo + 10, 105), Size = new Size(larguraCombo, 25), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CorFundoCampos, ForeColor = CorTextoClaro, FlatStyle = FlatStyle.Flat };
    cmbCaptureDevices.SelectedIndexChanged += (s, e) => { ReiniciarCapturaAudioExterno(); };

    Label lblSpotifyOutput = new Label() { Text = "Saída Áudio Externo:", Location = new Point(15 + (larguraCombo * 2) + 20, 85), Size = new Size(150, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    cmbSpotifyOutputDevice = new ComboBox() { Location = new Point(15 + (larguraCombo * 2) + 20, 105), Size = new Size(larguraCombo, 25), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CorFundoCampos, ForeColor = CorTextoClaro, FlatStyle = FlatStyle.Flat };
    cmbSpotifyOutputDevice.SelectedIndexChanged += (s, e) => { ReiniciarCapturaAudioExterno(); };

    grpConfiguracoes.Controls.AddRange(new Control[] { lblPasta, txtPasta, btnSelecionarPasta, lblMinutos, txtMinutos, lblDevice, cmbAudioDevices, lblCaptureDevice, cmbCaptureDevices, lblSpotifyOutput, cmbSpotifyOutputDevice });

    // --- SEÇÃO 2: AUTOMAÇÃO ---
    GroupBox grpAutomacao = new GroupBox() { Text = " ⏰ AUTOMAÇÃO POR HORÁRIO ", Location = new Point(margemPainelPrincipal, margemTopo + 145 + espacoEntreSecoes), Size = new Size(larguraPainelPrincipal, 95), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 10, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = CorFundoGroupBox };
    
    chkAutoHorario = new CheckBox() { Text = "Ativar controle automático de horário", Location = new Point(15, 25), Size = new Size(290, 20), ForeColor = CorTextoClaro };
    chkAutoHorario.CheckedChanged += ChkAutoHorario_CheckedChanged;

    Label lblInicio = new Label() { Text = "Iniciar às:", Location = new Point(15, 55), Size = new Size(65, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    dtpInicio = new DateTimePicker() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Location = new Point(85, 52), Size = new Size(70, 23), BackColor = CorFundoCampos, ForeColor = CorTextoClaro };

    Label lblFim = new Label() { Text = "Parar às:", Location = new Point(165, 55), Size = new Size(60, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    dtpFim = new DateTimePicker() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Location = new Point(230, 52), Size = new Size(70, 23), BackColor = CorFundoCampos, ForeColor = CorTextoClaro };

    btnAplicarHorario = new Button() { Text = "Aplicar Horários", Location = new Point(315, 50), Size = new Size(140, 26), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoNormal, ForeColor = Color.Gold, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    btnAplicarHorario.FlatAppearance.BorderColor = Color.Gold;
    btnAplicarHorario.Click += BtnAplicarHorario_Click;

    lblStatusAutomacao = new Label() { Text = "Modo Manual", Location = new Point(larguraPainelPrincipal - 165, 25), Size = new Size(150, 20), ForeColor = Color.Gray, Font = new Font("Segoe UI", 9, FontStyle.Italic), TextAlign = ContentAlignment.TopRight, Anchor = AnchorStyles.Top | AnchorStyles.Right };
    grpAutomacao.Controls.AddRange(new Control[] { chkAutoHorario, lblInicio, dtpInicio, lblFim, dtpFim, btnAplicarHorario, lblStatusAutomacao });

    // --- SEÇÃO 3: CONTROLES PRINCIPAIS ---
    GroupBox grpControles = new GroupBox() { Text = " 🎮 CONTROLES PRINCIPAIS ", Location = new Point(margemPainelPrincipal, margemTopo + 145 + 95 + (espacoEntreSecoes * 2)), Size = new Size(larguraPainelPrincipal, 65), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 10, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = CorFundoGroupBox };
    
    int larguraBotaoPrincipal = (larguraPainelPrincipal - 40) / 2;

    btnIniciar = new Button() { Text = "▶ LIGAR AGENDADOR", Location = new Point(15, 22), Size = new Size(larguraBotaoPrincipal, 32), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoSucesso, ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand };
    btnIniciar.FlatAppearance.BorderSize = 0;
    btnIniciar.Click += BtnIniciar_Click;

    btnParar = new Button() { Text = "⏹ DESLIGAR", Location = new Point(15 + larguraBotaoPrincipal + 10, 22), Size = new Size(larguraBotaoPrincipal, 32), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoPerigo, ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Enabled = false, Cursor = Cursors.Hand };
    btnParar.FlatAppearance.BorderSize = 0;
    btnParar.Click += BtnParar_Click;

    grpControles.Controls.AddRange(new Control[] { btnIniciar, btnParar });

    // --- SEÇÃO 4: MONITORAMENTO ---
    GroupBox grpStatus = new GroupBox() { Text = " 📊 MONITORAMENTO EM TEMPO REAL ", Location = new Point(margemPainelPrincipal, margemTopo + 145 + 95 + 65 + (espacoEntreSecoes * 3)), Size = new Size(larguraPainelPrincipal, 100), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 10, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = CorFundoGroupBox };
    Label lblProximoTitulo = new Label() { Text = "PRÓXIMO ÁUDIO:", Location = new Point(15, 22), Size = new Size(120, 15), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    
    lblContadorAudios = new Label() { Text = "[ 0 áudios ]", Location = new Point(larguraPainelPrincipal - 165, 22), Size = new Size(150, 15), ForeColor = Color.LightSkyBlue, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.TopRight, Anchor = AnchorStyles.Top | AnchorStyles.Right };

    lblProximoAudio = new Label() { Text = "Aguardando início...", Location = new Point(15, 42), Size = new Size(larguraPainelPrincipal - 30, 20), ForeColor = Color.Gold, Font = new Font("Segoe UI", 10, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    Label lblTempoTitulo = new Label() { Text = "TEMPO RESTANTE:", Location = new Point(15, 72), Size = new Size(120, 15), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    lblCronometro = new Label() { Text = "00:00", Location = new Point(140, 65), Size = new Size(100, 32), ForeColor = Color.Cyan, Font = new Font("Segoe UI", 18, FontStyle.Bold) };
    Label lblProximaHoraTitulo = new Label() { Text = "PRÓXIMO DISPARO ÀS:", Location = new Point(260, 72), Size = new Size(130, 15), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    lblProximoHorarioDisparo = new Label() { Text = "--:--:--", Location = new Point(395, 68), Size = new Size(95, 22), ForeColor = Color.LightGreen, Font = new Font("Segoe UI", 11, FontStyle.Bold) };

    grpStatus.Controls.AddRange(new Control[] { lblProximoTitulo, lblContadorAudios, lblProximoAudio, lblTempoTitulo, lblCronometro, lblProximaHoraTitulo, lblProximoHorarioDisparo });

    // --- SEÇÃO 5: REGISTRO DE EXECUÇÃO ---
    int topoLog = margemTopo + 145 + 95 + 65 + 100 + (espacoEntreSecoes * 4);
    Label lblLog = new Label() { Text = " 📋 HISTÓRICO DE EXECUÇÃO ", Location = new Point(margemPainelPrincipal, topoLog), Size = new Size(250, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
    
    int alturaRestanteLog = this.ClientSize.Height - topoLog - 65; 
    lstLog = new ListBox() { Location = new Point(margemPainelPrincipal, topoLog + 25), Size = new Size(larguraPainelPrincipal, alturaRestanteLog), BackColor = CorFundoCampos, ForeColor = CorTextoClaro, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };

    Label lblCreditos = new Label() { Text = "by: @ataliasloami", Location = new Point(margemPainelPrincipal, this.ClientSize.Height - 30), Size = new Size(larguraPainelPrincipal, 20), ForeColor = Color.FromArgb(100, 100, 104), Font = new Font("Segoe UI", 8, FontStyle.Italic), TextAlign = ContentAlignment.MiddleCenter, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };

    this.Controls.AddRange(new Control[] { 
        grpGatilho, grpVolumesBotoes, grpVolume,
        grpConfiguracoes, grpAutomacao, grpControles, grpStatus, lblLog, lstLog, lblCreditos 
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

        private void ListarDispositivosCaptura()
        {
            cmbCaptureDevices.Items.Clear();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                cmbCaptureDevices.Items.Add(new CaptureDeviceItem { Index = i, Name = caps.ProductName });
            }
            if (cmbCaptureDevices.Items.Count > 0) cmbCaptureDevices.SelectedIndex = 0;
        }

        private void ListarDispositivosSaidaAudioExterno()
        {
            cmbSpotifyOutputDevice.Items.Clear();
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var caps = WaveOut.GetCapabilities(i);
                cmbSpotifyOutputDevice.Items.Add(new DeviceItem { Index = i, Name = caps.ProductName });
            }
            if (cmbSpotifyOutputDevice.Items.Count > 0) cmbSpotifyOutputDevice.SelectedIndex = 0;
        }

        private void ConfigurarControlesVolume()
        {
            volumeUsuarioSpotify = 1.0f;
            volumeUsuarioVoz = 1.0f;
            volumeMinimoSpotify = 0.2f;
            volumeMinimoSpotifyBotao = 0.2f;
            atrasoDuckingMs = 0;
        }

        private void AtualizarVolumeSpotify()
        {
            lock (travaSpotify)
            {
                if (fadeSampleProvider != null)
                {
                    // Aplica o volume do usuário multiplicado pelo volume atual do ducking
                    fadeSampleProvider.Volume = volumeAtualSpotify * volumeUsuarioSpotify;
                }
            }
        }

        private void ReiniciarCapturaAudioExterno()
        {
            PararCapturaSpotify();
            System.Threading.Thread.Sleep(100); // Pequena pausa para garantir que tudo pare
            IniciarCapturaSpotify();
        }

        private void AtualizarVolumeVoz()
        {
            lock (travaAudio)
            {
                if (arquivoAudioAtual != null)
                {
                    arquivoAudioAtual.Volume = volumeUsuarioVoz;
                }
            }
        }

        private void AtualizarVolumeDisparo(int index)
        {
            lock (travaAudio)
            {
                // Se o áudio atual está tocando e é do botão especificado, atualiza o volume
                if (arquivoAudioAtual != null && indiceBotaoTocando == index && audioEstaTocando)
                {
                    arquivoAudioAtual.Volume = volumesDisparos[index];
                }
            }
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
                    
                    // Criar formulário para configurar tecla
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
            
            // Se já está tocando o mesmo botão, para o áudio
            if (indiceBotaoTocando == index && audioEstaTocando)
            {
                PararAudioAtual();
                indiceBotaoTocando = null;
                AdicionarLog($"[Disparo Imediato] Parado Botão {index + 1}");
                return;
            }
            
            indiceBotaoTocando = index;
            AdicionarLog($"[Disparo Imediato] Soltando Botão {index + 1}: {Path.GetFileName(caminhosDisparos[index])}");
            ReproduzirArquivoEspecifico(caminhosDisparos[index], false, volumeMinimoSpotifyBotao, volumesDisparos[index]);
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

        // --- MÉTODOS DO MIXER SPOTIFY (AUDIO DUCKING) ---
        private void IniciarCapturaSpotify()
        {
            try
            {
                lock (travaSpotify)
                {
                    if (spotifyCapturaAtiva) return;

                    // Obter dispositivo de captura selecionado
                    int dispositivoCapturaId = 0;
                    string textoSelecionado = cmbCaptureDevices.SelectedItem?.ToString() ?? "";
                    for (int i = 0; i < WaveIn.DeviceCount; i++)
                    {
                        var caps = WaveIn.GetCapabilities(i);
                        if ($"[{i}] {caps.ProductName}" == textoSelecionado) { dispositivoCapturaId = i; break; }
                    }

                    // Configurar WaveInEvent para capturar do Cabo Virtual
                    waveInSpotify = new WaveInEvent
                    {
                        DeviceNumber = dispositivoCapturaId,
                        WaveFormat = new WaveFormat(44100, 2) // 44.1kHz, estéreo
                    };

                    // Criar BufferedWaveProvider para armazenar os samples capturados
                    bufferedWaveProviderSpotify = new BufferedWaveProvider(waveInSpotify.WaveFormat)
                    {
                        BufferDuration = TimeSpan.FromSeconds(0.5), // Buffer de 500ms
                        DiscardOnBufferOverflow = true
                    };

                    // Criar FadeSampleProvider para controle de volume suave
                    fadeSampleProvider = new FadeSampleProvider(bufferedWaveProviderSpotify.ToSampleProvider());
                    fadeSampleProvider.Volume = 1.0f * volumeUsuarioSpotify; // Aplica volume do usuário
                    volumeAtualSpotify = 1.0f;

                    // Obter dispositivo de saída para o Spotify (dispositivo separado)
                    int dispositivoSaidaId = 0;
                    textoSelecionado = cmbSpotifyOutputDevice.SelectedItem?.ToString() ?? "";
                    for (int i = 0; i < WaveOut.DeviceCount; i++)
                    {
                        var caps = WaveOut.GetCapabilities(i);
                        if ($"[{i}] {caps.ProductName}" == textoSelecionado) { dispositivoSaidaId = i; break; }
                    }

                    // Configurar WaveOutEvent para enviar o áudio do Spotify para Voicemeeter
                    waveOutSpotify = new WaveOutEvent { DeviceNumber = dispositivoSaidaId };
                    waveOutSpotify.Init(fadeSampleProvider);

                    // Evento de captura de áudio com tratamento de erro
                    waveInSpotify.DataAvailable += (s, e) =>
                    {
                        try
                        {
                            lock (travaSpotify)
                            {
                                if (bufferedWaveProviderSpotify != null)
                                {
                                    bufferedWaveProviderSpotify.AddSamples(e.Buffer, 0, e.BytesRecorded);
                                }
                            }
                        }
                        catch { }
                    };

                    // Iniciar captura e reprodução
                    waveInSpotify.StartRecording();
                    waveOutSpotify.Play();
                    spotifyCapturaAtiva = true;

                    AdicionarLog($"Captura iniciada no dispositivo: [{dispositivoCapturaId}] {WaveIn.GetCapabilities(dispositivoCapturaId).ProductName}");
                }
            }
            catch (Exception ex)
            {
                AdicionarLog($"ERRO ao iniciar captura: {ex.Message}");
                // Limpar recursos em caso de erro
                try
                {
                    if (waveInSpotify != null) { waveInSpotify.Dispose(); waveInSpotify = null; }
                    if (waveOutSpotify != null) { waveOutSpotify.Dispose(); waveOutSpotify = null; }
                    bufferedWaveProviderSpotify = null;
                    fadeSampleProvider = null;
                    spotifyCapturaAtiva = false;
                }
                catch { }
            }
        }

        private void PararCapturaSpotify()
        {
            lock (travaSpotify)
            {
                if (!spotifyCapturaAtiva) return;

                try
                {
                    if (waveInSpotify != null)
                    {
                        waveInSpotify.StopRecording();
                        waveInSpotify.Dispose();
                        waveInSpotify = null;
                    }

                    if (waveOutSpotify != null)
                    {
                        waveOutSpotify.Stop();
                        waveOutSpotify.Dispose();
                        waveOutSpotify = null;
                    }

                    bufferedWaveProviderSpotify = null;
                    fadeSampleProvider = null;
                    spotifyCapturaAtiva = false;
                    volumeAtualSpotify = 1.0f;

                    AdicionarLog("Captura parada.");
                }
                catch (Exception ex)
                {
                    AdicionarLog($"ERRO ao parar captura: {ex.Message}");
                }
            }
        }

        private void FadeOutSpotify(float volumeDucking = 0.2f)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                // Aplicar atraso antes de iniciar o ducking
                if (atrasoDuckingMs > 0)
                {
                    System.Threading.Thread.Sleep(atrasoDuckingMs);
                }

                lock (travaSpotify)
                {
                    if (!spotifyCapturaAtiva || fadeSampleProvider == null) return;
                }

                float volumeInicial = volumeAtualSpotify;
                float volumeFinal = volumeDucking;
                int passos = 20; // Número de passos para o fade
                int intervaloMs = tempoTransicaoMs / passos;

                for (int i = 0; i <= passos; i++)
                {
                    lock (travaSpotify)
                    {
                        if (!spotifyCapturaAtiva || fadeSampleProvider == null) return;

                        float progresso = (float)i / passos;
                        volumeAtualSpotify = volumeInicial - (volumeInicial - volumeFinal) * progresso;
                        // Aplica o volume do ducking multiplicado pelo volume do usuário
                        fadeSampleProvider.Volume = volumeAtualSpotify * volumeUsuarioSpotify;
                    }
                    System.Threading.Thread.Sleep(intervaloMs);
                }
            });
        }

        private void FadeInSpotify()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                lock (travaSpotify)
                {
                    if (!spotifyCapturaAtiva || fadeSampleProvider == null) return;
                }

                float volumeInicial = volumeAtualSpotify;
                float volumeFinal = 1.0f;
                int passos = 20; // Número de passos para o fade
                int intervaloMs = tempoTransicaoMs / passos;

                for (int i = 0; i <= passos; i++)
                {
                    lock (travaSpotify)
                    {
                        if (!spotifyCapturaAtiva || fadeSampleProvider == null) return;

                        float progresso = (float)i / passos;
                        volumeAtualSpotify = volumeInicial + (volumeFinal - volumeInicial) * progresso;
                        // Aplica o volume do ducking multiplicado pelo volume do usuário
                        fadeSampleProvider.Volume = volumeAtualSpotify * volumeUsuarioSpotify;
                    }
                    System.Threading.Thread.Sleep(intervaloMs);
                }
            });
        }
        
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
                try
                {
                    if (dispositivoSaidaAtual != null)
                    {
                        if (dispositivoSaidaAtual.PlaybackState == PlaybackState.Playing)
                            dispositivoSaidaAtual.Stop();
                        dispositivoSaidaAtual.Dispose();
                        dispositivoSaidaAtual = null;
                    }
                }
                catch { dispositivoSaidaAtual = null; }
                
                try
                {
                    if (arquivoAudioAtual != null)
                    {
                        arquivoAudioAtual.Dispose();
                        arquivoAudioAtual = null;
                    }
                }
                catch { arquivoAudioAtual = null; }
                
                audioEstaTocando = false;
            }
        }

        private void ReproduzirArquivoEspecifico(string caminhoDoSom, bool ehDoAgendador = false, float volumeDuckingOverride = -1f, float volumeOverride = -1f)
        {
            if (!File.Exists(caminhoDoSom)) return;
            PararAudioAtual();

            // --- AUDIO DUCKING: Abaixar volume do Spotify antes de tocar a voz ---
            float volumeDuckingUsar = volumeDuckingOverride >= 0 ? volumeDuckingOverride : volumeMinimoSpotify;
            FadeOutSpotify(volumeDuckingUsar);

            int dispositivoIdReal = 0; string textoSelecionadoNaTela = cmbAudioDevices.SelectedItem?.ToString() ?? "";
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var caps = WaveOut.GetCapabilities(i);
                if ($"[{i}] {caps.ProductName}" == textoSelecionadoNaTela) { dispositivoIdReal = i; break; }
            }

            // Usar volume individual se fornecido, senão usar volume da voz
            float volumeUsar = volumeOverride >= 0 ? volumeOverride : volumeUsuarioVoz;

            System.Threading.Tasks.Task.Run(() => {
                try
                {
                    lock (travaAudio)
                    {
                        arquivoAudioAtual = new AudioFileReader(caminhoDoSom);
                        // Aplica o volume (individual do botão ou volume da voz)
                        arquivoAudioAtual.Volume = volumeUsar;
                        dispositivoSaidaAtual = new WaveOutEvent { DeviceNumber = dispositivoIdReal };
                        dispositivoSaidaAtual.Init(arquivoAudioAtual);
                        audioEstaTocando = true; dispositivoSaidaAtual.Play();
                    }

                    // Adicionar timeout de 5 minutos para evitar travamento
                    DateTime inicioReproducao = DateTime.Now;
                    TimeSpan timeout = TimeSpan.FromMinutes(5);

                    while (true)
                    {
                        lock (travaAudio) 
                        { 
                            if (dispositivoSaidaAtual == null || dispositivoSaidaAtual.PlaybackState != PlaybackState.Playing) 
                                break; 
                        }
                        
                        // Verificar timeout
                        if (DateTime.Now - inicioReproducao > timeout)
                        {
                            this.BeginInvoke(new Action(() => AdicionarLog("AVISO: Timeout de reprodução detectado. Forçando parada.")));
                            break;
                        }
                        
                        System.Threading.Thread.Sleep(100);
                    }
                }
                catch (Exception ex) { this.BeginInvoke(new Action(() => AdicionarLog($"AVISO ÁUDIO: {ex.Message}"))); }
                finally
                {
                    lock (travaAudio) { audioEstaTocando = false; }
                    
                    // Limpar índice do botão se não for do agendador
                    if (!ehDoAgendador)
                    {
                        indiceBotaoTocando = null;
                    }
                    
                    // --- AUDIO DUCKING: Subir volume do Spotify após a voz terminar ---
                    FadeInSpotify();
                    
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

    public class CaptureDeviceItem
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public override string ToString() => $"[{Index}] {Name}";
    }

    // --- CLASSE PARA CONTROLE DE VOLUME COM FADE SUAVE (AUDIO DUCKING) ---
    public class FadeSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private float volume = 1.0f;
        private readonly object lockObj = new object();

        public FadeSampleProvider(ISampleProvider source)
        {
            this.source = source;
            this.WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public float Volume
        {
            get
            {
                lock (lockObj) { return volume; }
            }
            set
            {
                lock (lockObj) { volume = Math.Clamp(value, 0.0f, 1.0f); }
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = source.Read(buffer, offset, count);
            lock (lockObj)
            {
                for (int i = 0; i < samplesRead; i++)
                {
                    buffer[offset + i] *= volume;
                }
            }
            return samplesRead;
        }
    }
}