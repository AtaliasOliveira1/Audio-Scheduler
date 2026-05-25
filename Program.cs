using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using NAudio.Wave;
using Microsoft.Data.Sqlite;

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

        // Componentes Visuais
        private TextBox txtPasta = null!;
        private NumericUpDown txtMinutos = null!;
        private Button btnSelecionarPasta = null!;
        private Button btnIniciar = null!;
        private Button btnParar = null!;
        private ListBox lstLog = null!;
        private ComboBox cmbAudioDevices = null!;

        // Componentes de Monitoramento
        private Label lblCronometro = null!;
        private Label lblProximoAudio = null!;
        private Label lblStatusAutomacao = null!;
        private Label lblProximoHorarioDisparo = null!;

        // Horários de Início e Fim
        private DateTimePicker dtpInicio = null!;
        private DateTimePicker dtpFim = null!;
        private CheckBox chkAutoHorario = null!;

        // Relógio Mestre
        private System.Windows.Forms.Timer relogioMestre = null!;

        // Variáveis de Controle
        private Random random = new Random();
        private string[] arquivosDisponiveis = Array.Empty<string>();
        private string proximoAudioCaminho = "";
        private int segundosRestantes = 0;
        private bool agendadorAtivoPeloHorario = false;

        // Paleta de Cores (Dark Mode)
        private readonly Color CorFundoJanela = Color.FromArgb(28, 28, 30);
        private readonly Color CorFundoCampos = Color.FromArgb(44, 44, 46);
        private readonly Color CorTextoClaro = Color.FromArgb(242, 242, 247);
        private readonly Color CorTextoEscuro = Color.FromArgb(209, 209, 214);
        private readonly Color CorBotaoSucesso = Color.FromArgb(52, 199, 89);
        private readonly Color CorBotaoPerigo = Color.FromArgb(255, 59, 48);
        private readonly Color CorBotaoNormal = Color.FromArgb(58, 58, 60);

        public JanelaPrincipal()
        {
            this.Text = "AudioScheduler - by: @ataliasloami";
            this.Size = new Size(550, 720); 
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = CorFundoJanela;

            CriarComponentesVisuais();
            ListarDispositivosAudio();
            ConfigurarRelogioMestre();

            ConfigurarBancoSQLite();
            CarregarConfiguracoesSalvas();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            
            Application.DoEvents();
            System.Threading.Thread.Sleep(600); 

            if (!string.IsNullOrEmpty(txtPasta.Text))
            {
                AdicionarLog("Banco de dados SQLite lido! Ativando monitoramento automático...");
                BtnIniciar_Click(this, EventArgs.Empty);
            }
            else
            {
                AdicionarLog("Pronto para uso. Selecione a pasta e o dispositivo para começar.");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
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
                            Pasta TEXT,
                            Minutos INTEGER,
                            Dispositivo TEXT,
                            UsarHorario TEXT,
                            HoraInicio TEXT,
                            HoraFim TEXT
                        );";
                    
                    using (var comando = new SqliteCommand(queryTabela, conexao))
                    {
                        comando.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar o banco SQLite: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SalvarConfiguracoesAtuais()
        {
            try
            {
                using (var conexao = new SqliteConnection(STRING_CONEXAO))
                {
                    conexao.Open();
                    
                    string queryLimpar = "DELETE FROM Config;";
                    using (var cmdLimpar = new SqliteCommand(queryLimpar, conexao)) { cmdLimpar.ExecuteNonQuery(); }

                    string queryInserir = @"
                        INSERT INTO Config (Pasta, Minutos, Dispositivo, UsarHorario, HoraInicio, HoraFim) 
                        VALUES (@pasta, @minutos, @dispositivo, @usarHorario, @horaInicio, @horaFim);";

                    using (var cmdInserir = new SqliteCommand(queryInserir, conexao))
                    {
                        cmdInserir.Parameters.AddWithValue("@pasta", txtPasta.Text);
                        cmdInserir.Parameters.AddWithValue("@minutos", (int)txtMinutos.Value);
                        cmdInserir.Parameters.AddWithValue("@dispositivo", cmbAudioDevices.SelectedItem?.ToString() ?? "");
                        cmdInserir.Parameters.AddWithValue("@usarHorario", chkAutoHorario.Checked.ToString());
                        cmdInserir.Parameters.AddWithValue("@horaInicio", dtpInicio.Value.ToString("HH:mm"));
                        cmdInserir.Parameters.AddWithValue("@horaFim", dtpFim.Value.ToString("HH:mm"));
                        
                        cmdInserir.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro ao salvar no SQLite: " + ex.Message);
            }
        }

        private void CarregarConfiguracoesSalvas()
        {
            try
            {
                using (var conexao = new SqliteConnection(STRING_CONEXAO))
                {
                    conexao.Open();
                    string querySelecionar = "SELECT Pasta, Minutos, Dispositivo, UsarHorario, HoraInicio, HoraFim FROM Config LIMIT 1;";

                    using (var comando = new SqliteCommand(querySelecionar, conexao))
                    using (var resultado = comando.ExecuteReader())
                    {
                        if (resultado.Read())
                        {
                            txtPasta.Text = resultado["Pasta"].ToString() ?? "";
                            AtualizarListaDeArquivos();

                            if (int.TryParse(resultado["Minutos"].ToString(), out int minutos))
                                txtMinutos.Value = minutos;

                            string dispositivoSalvo = resultado["Dispositivo"].ToString() ?? "";
                            for (int i = 0; i < cmbAudioDevices.Items.Count; i++)
                            {
                                if (cmbAudioDevices.Items[i]?.ToString() == dispositivoSalvo)
                                {
                                    cmbAudioDevices.SelectedIndex = i;
                                    break;
                                }
                            }

                            if (bool.TryParse(resultado["UsarHorario"].ToString(), out bool usarHorario))
                                chkAutoHorario.Checked = usarHorario;

                            if (DateTime.TryParseExact(resultado["HoraInicio"].ToString(), "HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime horaInicio))
                                dtpInicio.Value = DateTime.Today.Add(horaInicio.TimeOfDay);

                            if (DateTime.TryParseExact(resultado["HoraFim"].ToString(), "HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime horaFim))
                                dtpFim.Value = DateTime.Today.Add(horaFim.TimeOfDay);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AdicionarLog($"Erro ao ler SQLite: {ex.Message}");
            }
        }

        private void CriarComponentesVisuais()
        {
            // --- Pasta dos Áudios ---
            Label lblPasta = new Label() { Text = "Pasta dos Áudios:", Location = new Point(20, 15), Size = new Size(150, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            txtPasta = new TextBox() { Location = new Point(20, 35), Size = new Size(390, 25), ReadOnly = true, BackColor = CorFundoCampos, ForeColor = CorTextoClaro, BorderStyle = BorderStyle.FixedSingle };
            btnSelecionarPasta = new Button() { Text = "Buscar...", Location = new Point(420, 34), Size = new Size(90, 26), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoNormal, ForeColor = CorTextoClaro };
            btnSelecionarPasta.FlatAppearance.BorderColor = CorFundoCampos;
            btnSelecionarPasta.Click += SelecionarPasta_Click;

            // --- Dispositivo de Saída ---
            Label lblDevice = new Label() { Text = "Dispositivo de Saída (Voicemeeter):", Location = new Point(20, 75), Size = new Size(300, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            cmbAudioDevices = new ComboBox() { Location = new Point(20, 95), Size = new Size(490, 25), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CorFundoCampos, ForeColor = CorTextoClaro, FlatStyle = FlatStyle.Flat };

            // --- Intervalo em Minutos ---
            Label lblMinutos = new Label() { Text = "Intervalo (Minutos):", Location = new Point(20, 135), Size = new Size(130, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            txtMinutos = new NumericUpDown() { Location = new Point(20, 155), Size = new Size(80, 25), Minimum = 1, Maximum = 60, Value = 5, BackColor = CorFundoCampos, ForeColor = CorTextoClaro, BorderStyle = BorderStyle.FixedSingle };

            // --- Painel: Automação por Horário ---
            GroupBox grpAutomacao = new GroupBox() { Text = " AUTOMAÇÃO POR HORÁRIO ", Location = new Point(20, 195), Size = new Size(490, 110), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            
            chkAutoHorario = new CheckBox() { Text = "Ativar controle automático de horário", Location = new Point(15, 22), Size = new Size(300, 20), ForeColor = Color.LightSkyBlue };
            chkAutoHorario.CheckedChanged += ChkAutoHorario_CheckedChanged;

            Label lblInicio = new Label() { Text = "Iniciar às:", Location = new Point(15, 52), Size = new Size(65, 20), ForeColor = CorTextoEscuro };
            dtpInicio = new DateTimePicker() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Location = new Point(85, 49), Size = new Size(70, 23), BackColor = CorFundoCampos, ForeColor = CorTextoClaro };

            Label lblFim = new Label() { Text = "Parar às:", Location = new Point(175, 52), Size = new Size(60, 20), ForeColor = CorTextoEscuro };
            dtpFim = new DateTimePicker() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Location = new Point(240, 49), Size = new Size(70, 23), BackColor = CorFundoCampos, ForeColor = CorTextoClaro };

            lblStatusAutomacao = new Label() { Text = "Modo Manual", Location = new Point(330, 52), Size = new Size(140, 20), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8, FontStyle.Italic) };
            Label lblAvisoReinicio = new Label() { Text = "*Caso altere os horários acima, reinicie o programa para aplicar.", Location = new Point(15, 82), Size = new Size(460, 18), ForeColor = Color.FromArgb(235, 94, 40), Font = new Font("Segoe UI", 8, FontStyle.Bold) };

            grpAutomacao.Controls.AddRange(new Control[] { chkAutoHorario, lblInicio, dtpInicio, lblFim, dtpFim, lblStatusAutomacao, lblAvisoReinicio });

            // --- Botões de Controle ---
            btnIniciar = new Button() { Text = "LIGAR AGENDADOR", Location = new Point(20, 320), Size = new Size(235, 32), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoSucesso, ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            btnIniciar.Click += BtnIniciar_Click;

            btnParar = new Button() { Text = "DESLIGAR", Location = new Point(275, 320), Size = new Size(235, 32), FlatStyle = FlatStyle.Flat, BackColor = CorBotaoPerigo, ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Enabled = false };
            btnParar.Click += BtnParar_Click;

            // --- Painel de Monitoramento ---
            GroupBox grpStatus = new GroupBox() { Text = " MONITORAMENTO EM TEMPO REAL ", Location = new Point(20, 365), Size = new Size(490, 130), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            
            Label lblProximoTitulo = new Label() { Text = "PRÓXIMO ÁUDIO NA AGULHA:", Location = new Point(15, 22), Size = new Size(200, 15), ForeColor = CorTextoEscuro, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            lblProximoAudio = new Label() { Text = "Aguardando início...", Location = new Point(15, 40), Size = new Size(460, 20), ForeColor = Color.Gold, Font = new Font("Segoe UI", 10, FontStyle.Bold) };

            Label lblTempoTitulo = new Label() { Text = "TEMPO RESTANTE:", Location = new Point(15, 78), Size = new Size(120, 15), ForeColor = CorTextoEscuro, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            lblCronometro = new Label() { Text = "00:00", Location = new Point(135, 68), Size = new Size(100, 32), ForeColor = Color.Cyan, Font = new Font("Segoe UI", 18, FontStyle.Bold) };

            Label lblProximaHoraTitulo = new Label() { Text = "PRÓXIMO DISPARO ÀS:", Location = new Point(245, 78), Size = new Size(130, 15), ForeColor = CorTextoEscuro, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            lblProximoHorarioDisparo = new Label() { Text = "--:--:--", Location = new Point(380, 74), Size = new Size(95, 22), ForeColor = Color.LightGreen, Font = new Font("Segoe UI", 11, FontStyle.Bold) };

            grpStatus.Controls.AddRange(new Control[] { lblProximoTitulo, lblProximoAudio, lblTempoTitulo, lblCronometro, lblProximaHoraTitulo, lblProximoHorarioDisparo });

            // --- Histórico de Execução ---
            Label lblLog = new Label() { Text = "Histórico de Execução:", Location = new Point(20, 510), Size = new Size(200, 20), ForeColor = CorTextoClaro, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            lstLog = new ListBox() { Location = new Point(20, 530), Size = new Size(490, 110), BackColor = CorFundoCampos, ForeColor = CorTextoClaro, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9) };

            // Rodapé de Créditos
            Label lblCreditos = new Label() { Text = "by: @ataliasloami", Location = new Point(20, 650), Size = new Size(490, 20), ForeColor = Color.FromArgb(100, 100, 104), Font = new Font("Segoe UI", 8, FontStyle.Italic), TextAlign = ContentAlignment.MiddleCenter };

            this.Controls.AddRange(new Control[] { 
                lblPasta, txtPasta, btnSelecionarPasta, 
                lblDevice, cmbAudioDevices,
                lblMinutos, txtMinutos, grpAutomacao, btnIniciar, btnParar, 
                grpStatus, lblLog, lstLog, lblCreditos 
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
            if (chkAutoHorario.Checked)
            {
                lblStatusAutomacao.Text = "Monitorando...";
                lblStatusAutomacao.ForeColor = Color.Orange;
            }
            else
            {
                lblStatusAutomacao.Text = "Modo Manual";
                lblStatusAutomacao.ForeColor = Color.Gray;
            }
        }

        private void SelecionarPasta_Click(object? sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtPasta.Text = fbd.SelectedPath;
                    AtualizarListaDeArquivos();
                }
            }
        }

        private void AtualizarListaDeArquivos()
        {
            string[] formatosSuportados = { ".mp3", ".wav" };
            try
            {
                if (!string.IsNullOrEmpty(txtPasta.Text) && Directory.Exists(txtPasta.Text))
                {
                    arquivosDisponiveis = Directory.GetFiles(txtPasta.Text)
                                                   .Where(file => formatosSuportados.Contains(Path.GetExtension(file).ToLower()))
                                                   .ToArray();
                }
            }
            catch
            {
                arquivosDisponiveis = Array.Empty<string>();
            }
        }

        private void SortearProximoAudio()
        {
            AtualizarListaDeArquivos();
            if (arquivosDisponiveis.Length == 0)
            {
                lblProximoAudio.Text = "Buscando arquivos na pasta...";
                proximoAudioCaminho = "";
                return;
            }
            int indice = random.Next(arquivosDisponiveis.Length);
            proximoAudioCaminho = arquivosDisponiveis[indice];
            lblProximoAudio.Text = Path.GetFileName(proximoAudioCaminho);
        }

        private void BtnIniciar_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtPasta.Text))
            {
                AdicionarLog("Aviso: Selecione uma pasta válida para ligar o agendador.");
                return;
            }

            btnSelecionarPasta.Enabled = false;
            txtMinutos.Enabled = false;
            cmbAudioDevices.Enabled = false;
            chkAutoHorario.Enabled = false;
            dtpInicio.Enabled = false;
            dtpFim.Enabled = false;
            btnIniciar.Enabled = false;
            btnParar.Enabled = true;

            agendadorAtivoPeloHorario = false; 
            relogioMestre.Start();

            if (chkAutoHorario.Checked)
            {
                AdicionarLog("Modo Automático Ativo.");
                lblStatusAutomacao.Text = "Monitorando Ativo";
                lblStatusAutomacao.ForeColor = Color.LimeGreen;
                
                VerificarJanelaDeHorario();
            }
            else
            {
                SortearProximoAudio();
                ExecutarToqueDoAudioSorteado();
                
                SortearProximoAudio();
                segundosRestantes = (int)txtMinutos.Value * 60;
                CalcularEExibirProximoHorario();
                AtualizarTextoCronometro();
                AdicionarLog($"Agendador manual ligado! Ciclo: {txtMinutos.Value} min.");
            }
        }

        private void BtnParar_Click(object? sender, EventArgs e)
        {
            relogioMestre.Stop();

            btnSelecionarPasta.Enabled = true;
            txtMinutos.Enabled = true;
            cmbAudioDevices.Enabled = true;
            chkAutoHorario.Enabled = true;
            dtpInicio.Enabled = true;
            dtpFim.Enabled = true;

            btnIniciar.Enabled = true;
            btnParar.Enabled = false;

            segundosRestantes = 0;
            agendadorAtivoPeloHorario = false;
            proximoAudioCaminho = "";

            lblCronometro.Text = "00:00";
            lblProximoAudio.Text = "Agendador desligado.";
            lblProximoHorarioDisparo.Text = "--:--:--";
            
            if (chkAutoHorario.Checked)
            {
                lblStatusAutomacao.Text = "Monitorando...";
                lblStatusAutomacao.ForeColor = Color.Orange;
            }
            else
            {
                lblStatusAutomacao.Text = "Modo Manual";
                lblStatusAutomacao.ForeColor = Color.Gray;
            }

            AdicionarLog("Agendador parado e limpo com sucesso.");
        }

        private void RelogioMestre_Tick(object? sender, EventArgs e)
        {
            if (chkAutoHorario.Checked)
            {
                VerificarJanelaDeHorario();
            }

            if (!chkAutoHorario.Checked || agendadorAtivoPeloHorario)
            {
                if (segundosRestantes > 0)
                {
                    segundosRestantes--;
                    AtualizarTextoCronometro();

                    if (segundosRestantes == 0)
                    {
                        ExecutarToqueDoAudioSorteado();
                        
                        // CORREÇÃO DA LINHA 370: Mudado de 'archivosDisponiveis' para 'arquivosDisponiveis'
                        SortearProximoAudio();
                        segundosRestantes = (int)txtMinutos.Value * 60;
                        CalcularEExibirProximoHorario();
                        AtualizarTextoCronometro();
                    }
                }
            }
        }

        private void VerificarJanelaDeHorario()
        {
            TimeSpan horaAtual = DateTime.Now.TimeOfDay;
            TimeSpan horaInicio = dtpInicio.Value.TimeOfDay;
            TimeSpan horaFim = dtpFim.Value.TimeOfDay;

            bool estaNaJanelaDeTempo = false;

            if (horaInicio <= horaFim)
                estaNaJanelaDeTempo = (horaAtual >= horaInicio && horaAtual <= horaFim);
            else
                estaNaJanelaDeTempo = (horaAtual >= horaInicio || horaAtual <= horaFim);

            if (!estaNaJanelaDeTempo)
            {
                if (agendadorAtivoPeloHorario || lblProximoAudio.Text == "Aguardando início...")
                {
                    AdicionarLog("Fora da janela de horário programada.");
                    agendadorAtivoPeloHorario = false;
                    lblProximoAudio.Text = "Fora do horário de funcionamento.";
                    lblCronometro.Text = "--:--";
                    lblProximoHorarioDisparo.Text = "--:--:--";
                    segundosRestantes = 0;
                }
            }
            else
            {
                if (!agendadorAtivoPeloHorario)
                {
                    AdicionarLog("Horário válido atingido! Disparando áudio inicial...");
                    agendadorAtivoPeloHorario = true;
                    
                    SortearProximoAudio();
                    ExecutarToqueDoAudioSorteado();
                    
                    SortearProximoAudio();
                    segundosRestantes = (int)txtMinutos.Value * 60;
                    CalcularEExibirProximoHorario();
                    AtualizarTextoCronometro();
                }
            }
        }

        private void CalcularEExibirProximoHorario()
        {
            DateTime horarioProximoToque = DateTime.Now.AddSeconds(segundosRestantes);
            lblProximoHorarioDisparo.Text = horarioProximoToque.ToString("HH:mm:ss");
        }

        private void AtualizarTextoCronometro()
        {
            TimeSpan tempo = TimeSpan.FromSeconds(segundosRestantes);
            lblCronometro.Text = tempo.ToString(@"mm\:ss");
        }

        private void ExecutarToqueDoAudioSorteado()
        {
            AtualizarListaDeArquivos();

            if (string.IsNullOrEmpty(proximoAudioCaminho) || !File.Exists(proximoAudioCaminho))
            {
                AdicionarLog("Aviso: O arquivo sorteado sumiu, tentando re-sortear...");
                SortearProximoAudio();
                if (string.IsNullOrEmpty(proximoAudioCaminho)) return;
            }

            string nomeArquivo = Path.GetFileName(proximoAudioCaminho);
            AdicionarLog($"Tentando reproduzir: {nomeArquivo}");

            int dispositivoIdReal = 0; 
            string textoSelecionadoNaTela = cmbAudioDevices.SelectedItem?.ToString() ?? "";

            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var caps = WaveOut.GetCapabilities(i);
                string nomeMontado = $"[{i}] {caps.ProductName}";
                if (nomeMontado == textoSelecionadoNaTela)
                {
                    dispositivoIdReal = i;
                    break;
                }
            }

            try
            {
                using (var audioFile = new AudioFileReader(proximoAudioCaminho))
                using (var outputDevice = new WaveOutEvent { DeviceNumber = dispositivoIdReal }) 
                {
                    outputDevice.Init(audioFile);
                    outputDevice.Play();

                    while (outputDevice.PlaybackState == PlaybackState.Playing)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(100);
                    }
                }
                AdicionarLog("Áudio concluído com sucesso.");
            }
            catch (Exception ex)
            {
                AdicionarLog($"ERRO CRÍTICO NA REPRODUÇÃO: {ex.Message}");
            }
        }

        private void AdicionarLog(string message)
        {
            string hora = DateTime.Now.ToString("HH:mm:ss");
            lstLog.Items.Add($"[{hora}] {message}");
            lstLog.TopIndex = lstLog.Items.Count - 1;
        }
    }

    public class DeviceItem
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public override string ToString() => $"[{Index}] {Name}";
    }
}