import os
import json
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, simpledialog
from datetime import datetime, timedelta
import time
import requests
import webbrowser
import pygame
from PIL import Image, ImageTk
import weakref
import gc
import sys

# Configuração específica para Windows - força uso do DirectSound
if sys.platform.startswith('win'):
    os.environ['SDL_AUDIODRIVER'] = 'directsound'

# DESIGN, ÁUDIO E AGENDAMENTO
import customtkinter as ctk
try:
    import sounddevice as sd
    SOUNDDEVICE_AVAILABLE = True
except ImportError:
    SOUNDDEVICE_AVAILABLE = False
    print("Aviso: sounddevice não disponível - funcionalidade de seleção de dispositivo limitada")

# Define o tema padrão
ctk.set_appearance_mode("Dark")
ctk.set_default_color_theme("blue")

# INFORMAÇÕES DO PROGRAMA E CHAVES DE API
VERSION = "3.0"

# Configurações do Baserow
BASEROW_API_URL = "https://api.baserow.io/api/database/rows/table/"
BASEROW_LICENSE_TABLE_ID = "682031"
BASEROW_UPDATE_TABLE_ID = "682052"
BASEROW_TOKEN = "dUragpDUHMQvaB9tmJu2a8Wzk09EIrnZ"

# Arquivos de configurações locais
SAVE_FILE = "audio_list.json"
ALERT_SOUND = "alert.wav"
LICENSE_FILE = "license.json"

# Intervalo de verificação de licença online (em segundos)
LICENSE_CHECK_INTERVAL = 12 * 3600  # 12 horas

# Cache para sons carregados - otimizado
_sound_cache = {}
_sound_cache_lock = threading.Lock()

def clear_sound_cache():
    """Limpa o cache de sons de forma thread-safe."""
    with _sound_cache_lock:
        _sound_cache.clear()
    gc.collect()

def get_cached_sound(path):
    """Retorna um som do cache ou carrega e armazena no cache de forma thread-safe."""
    with _sound_cache_lock:
        if path in _sound_cache:
            return _sound_cache[path]
        
        try:
            # Limita o cache a 5 sons para evitar uso excessivo de memória
            if len(_sound_cache) >= 5:
                # Remove o primeiro item (FIFO)
                oldest_key = next(iter(_sound_cache))
                del _sound_cache[oldest_key]
            
            sound = pygame.mixer.Sound(path)
            _sound_cache[path] = sound
            return sound
            
        except Exception as e:
            raise e

class AudioMixerManager:
    """Gerenciador robusto do mixer de áudio."""
    
    def __init__(self):
        self.initialized = False
        self.current_volume = 0.5
        
    def initialize(self):
        """Inicializa o mixer com fallback para diferentes configurações."""
        configs = [
            # Configurações em ordem de prioridade
            {'frequency': 44100, 'size': -16, 'channels': 2, 'buffer': 1024},
            {'frequency': 22050, 'size': -16, 'channels': 2, 'buffer': 1024},
            {'frequency': 44100, 'size': -16, 'channels': 1, 'buffer': 1024},
            {'frequency': 22050, 'size': -16, 'channels': 1, 'buffer': 2048},
        ]
        
        for config in configs:
            try:
                pygame.mixer.quit()  # Garante que está limpo
                time.sleep(0.1)  # Pequena pausa
                pygame.mixer.init(**config)
                
                # Testa se realmente funcionou
                info = pygame.mixer.get_init()
                if info:
                    self.initialized = True
                    print(f"Mixer inicializado com sucesso: {info}")
                    return True
                    
            except Exception as e:
                print(f"Falha na configuração {config}: {e}")
                continue
        
        print("ERRO: Não foi possível inicializar o mixer com nenhuma configuração")
        return False
    
    def set_volume(self, volume):
        """Define o volume de forma robusta."""
        self.current_volume = max(0.0, min(1.0, volume))
        try:
            pygame.mixer.music.set_volume(self.current_volume)
        except:
            pass
    
    def play_sound(self, sound_path, wait_finish=True):
        """Toca um som de forma robusta."""
        if not self.initialized:
            if not self.initialize():
                return False
        
        try:
            sound = get_cached_sound(sound_path)
            sound.set_volume(self.current_volume)
            
            channel = sound.play()
            if not channel:
                return False
            
            if wait_finish:
                while channel.get_busy():
                    time.sleep(0.01)
            
            return True
            
        except Exception as e:
            print(f"Erro ao tocar som {sound_path}: {e}")
            return False
    
    def stop_all(self):
        """Para todos os sons."""
        try:
            pygame.mixer.stop()
        except:
            pass

# Instância global do gerenciador de mixer
audio_manager = AudioMixerManager()

class AudioItem:
    """Representa um item de áudio na lista."""
    def __init__(self, path, interval):
        self.path = path
        self.interval = interval

class AudioItemFrame(ctk.CTkFrame):
    """Widget customizado para cada item de áudio na lista."""
    
    def __init__(self, master, app, audio_item, index):
        super().__init__(master, corner_radius=10, fg_color="#363636")
        self.app = weakref.ref(app)
        self.audio_item = audio_item
        self.index = index
        self.is_valid = os.path.exists(audio_item.path)
        self.create_widgets()

    def create_widgets(self):
        nome_arquivo = os.path.basename(self.audio_item.path)
        
        # Nome do arquivo
        color = "#C0C0C0" if self.is_valid else "red"
        display_name = nome_arquivo if self.is_valid else f"{nome_arquivo} (Arquivo não encontrado)"
        
        self.label_nome = ctk.CTkLabel(self, text=display_name, font=("Arial", 12, "bold"), text_color=color)
        self.label_nome.grid(row=0, column=0, padx=10, pady=5, sticky="w")
        
        # Intervalo
        if self.is_valid:
            self.label_intervalo = ctk.CTkLabel(self, text=f"a cada {self.audio_item.interval} min", text_color="#C0C0C0")
        else:
            self.label_intervalo = ctk.CTkLabel(self, text="Item desativado.", text_color="red")
        self.label_intervalo.grid(row=1, column=0, padx=10, pady=(0, 5), sticky="w")
        
        # Botões
        button_frame = ctk.CTkFrame(self, fg_color="transparent")
        button_frame.grid(row=0, column=1, rowspan=2, padx=10, pady=5, sticky="e")

        self.btn_remover = ctk.CTkButton(button_frame, text="Remover", command=self.remover_item, width=70)
        self.btn_remover.pack(side="right")
        
        if self.is_valid:
            self.btn_editar = ctk.CTkButton(button_frame, text="Editar", command=self.editar_item, width=70)
            self.btn_editar.pack(side="right", padx=(0, 5))
            
            btn_up = ctk.CTkButton(button_frame, text="▲", command=self.move_up, width=30)
            btn_up.pack(side="right", padx=(0, 5))
            
            btn_down = ctk.CTkButton(button_frame, text="▼", command=self.move_down, width=30)
            btn_down.pack(side="right", padx=(0, 5))

        self.grid_columnconfigure(0, weight=1)

    def remover_item(self):
        app = self.app()
        if app:
            app.remover_audio(self.index)

    def editar_item(self):
        app = self.app()
        if app:
            app.editar_audio(self.index)
        
    def move_up(self):
        app = self.app()
        if app:
            app.move_audio(self.index, -1)
        
    def move_down(self):
        app = self.app()
        if app:
            app.move_audio(self.index, 1)

class PreciseTimer:
    """Timer preciso para contagem regressiva."""
    
    def __init__(self, duration_seconds, callback=None):
        self.duration = duration_seconds
        self.start_time = time.time()
        self.callback = callback
        self.is_running = True
    
    def get_remaining(self):
        """Retorna o tempo restante em segundos."""
        if not self.is_running:
            return 0
        
        elapsed = time.time() - self.start_time
        remaining = max(0, self.duration - elapsed)
        
        if remaining == 0 and self.callback:
            self.callback()
            self.is_running = False
        
        return remaining
    
    def stop(self):
        """Para o timer."""
        self.is_running = False

class AudioSchedulerApp:
    """A classe principal da aplicação."""
    
    def __init__(self, root):
        self.root = root
        self.root.title(f"ÁudioScheduler - by @ataliasloami v{VERSION}")
        self.root.geometry("800x660")
        self.root.resizable(False, False)

        # Estados principais
        self.audio_items = []
        self.running = False
        self.current_index = -1
        self.item_frames = []
        
        # Controles de threads e sincronização
        self.play_thread = None
        self.shutdown_event = threading.Event()
        self.timer_lock = threading.Lock()
        self.current_timer = None
        
        # Variáveis de interface
        self.alert_enabled = tk.BooleanVar(value=True)
        self.volume_level = ctk.DoubleVar(value=0.5)
        self.agendamento_ativo = ctk.BooleanVar(value=False)
        self.saida_selecionada = ctk.StringVar()
        
        # Inicialização
        self.device_list_output = ["Padrão do Sistema"]
        self.next_execution_time = None
        
        # Inicializa mixer
        audio_manager.initialize()
        
        # Constrói interface
        self.iniciar_interface_principal()

        # Verificações de licença e atualização
        if self.verificar_ativacao_hibrida():
            self.log_message("Licença validada. O programa está pronto para uso.", "info")
            self.verificar_atualizacao()
        else:
            self.exibir_janela_ativacao()
            
        self.root.protocol("WM_DELETE_WINDOW", self.on_closing)

    def log_message(self, message, message_type="info"):
        """Adiciona uma mensagem ao painel de logs com timestamp."""
        if not hasattr(self, 'log_box'):
            return
            
        try:
            self.log_box.configure(state="normal")
            
            # Limita o log a 50 linhas
            content = self.log_box.get("1.0", "end-1c")
            lines = content.split('\n')
            if len(lines) > 50:
                self.log_box.delete("1.0", f"{len(lines)-40}.0")
            
            timestamp = datetime.now().strftime("%H:%M:%S")
            self.log_box.insert("end", f"[{timestamp}] - {message}\n", (message_type,))
            self.log_box.configure(state="disabled")
            self.log_box.yview_moveto(1.0)
        except:
            pass  # Ignora erros de interface destruída

    def verificar_atualizacao(self):
        """Verifica atualizações em thread separada."""
        def check_update():
            try:
                headers = {"Authorization": f"Token {BASEROW_TOKEN}"}
                response = requests.get(
                    f"{BASEROW_API_URL}{BASEROW_UPDATE_TABLE_ID}/?user_field_names=true", 
                    headers=headers, timeout=10
                )
                response.raise_for_status()
                data = response.json()
                
                if data.get("results"):
                    ultima_versao_info = data["results"][0]
                    versao_online = ultima_versao_info.get("versao")
                    link_atualizacao = ultima_versao_info.get("link")
                    
                    if versao_online and link_atualizacao and float(versao_online) > float(VERSION):
                        self.root.after(0, lambda: self._show_update_dialog(versao_online, link_atualizacao))
                
            except Exception:
                self.root.after(0, lambda: self.log_message("Não foi possível verificar atualizações.", "warning"))
        
        threading.Thread(target=check_update, daemon=True).start()

    def _show_update_dialog(self, versao_online, link_atualizacao):
        """Exibe diálogo de atualização na thread principal."""
        resposta = messagebox.askyesno(
            "Nova Atualização Disponível!",
            f"Nova versão ({versao_online}) disponível. Atual: {VERSION}.\n\nAbrir link para download?"
        )
        if resposta:
            webbrowser.open(link_atualizacao)

    def verificar_ativacao_hibrida(self):
        """Verifica licença local e online."""
        licenca_valida_local = self.verificar_ativacao_local()
        if licenca_valida_local:
            try:
                with open(LICENSE_FILE, "r") as f:
                    licenca = json.load(f)
                ultima_verificacao_str = licenca.get("ultima_verificacao_online")
                if ultima_verificacao_str:
                    ultima_verificacao = datetime.strptime(ultima_verificacao_str, "%Y-%m-%d %H:%M:%S")
                    if (datetime.now() - ultima_verificacao).total_seconds() < LICENSE_CHECK_INTERVAL:
                        # Agenda próxima verificação
                        self.root.after(int(LICENSE_CHECK_INTERVAL * 1000), 
                                      lambda: self.verificar_no_baserow(True))
                        return True
            except:
                pass
        return self.verificar_no_baserow(licenca_valida_local)

    def verificar_ativacao_local(self):
        """Verifica licença local."""
        if not os.path.exists(LICENSE_FILE):
            return False
        try:
            with open(LICENSE_FILE, "r") as f:
                licenca = json.load(f)
            data_expiracao_str = licenca.get("data_expiracao")
            if not data_expiracao_str:
                return False
                
            hoje = datetime.now().date()
            data_expiracao = datetime.strptime(data_expiracao_str, "%Y-%m-%d").date()
            
            if hoje > data_expiracao:
                messagebox.showerror("Licença Expirada", 
                    "Sua licença expirou. Contate:\n\nAtalias Lô-Amí\n(99)98469-1168")
                return False
            return True
        except:
            return False
            
    def verificar_no_baserow(self, licenca_local_existente=False):
        """Verifica licença no servidor."""
        try:
            headers = {"Authorization": f"Token {BASEROW_TOKEN}"}
            response = requests.get(
                f"{BASEROW_API_URL}{BASEROW_LICENSE_TABLE_ID}/?user_field_names=true", 
                headers=headers, timeout=10
            )
            response.raise_for_status()
            data = response.json()
            
            if licenca_local_existente:
                with open(LICENSE_FILE, "r") as f:
                    licenca_local = json.load(f)
                    
                found_license = None
                for row in data.get("results", []):
                    if (row.get("validade") == licenca_local.get("data_expiracao") and 
                        row.get("ativa")):
                        found_license = row
                        break
                        
                if found_license:
                    licenca_local["ultima_verificacao_online"] = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
                    with open(LICENSE_FILE, "w") as f:
                        json.dump(licenca_local, f, indent=2)
                    self.log_message("Licença verificada online: Ativa.", "info")
                    self.root.after(int(LICENSE_CHECK_INTERVAL * 1000), 
                                  lambda: self.verificar_no_baserow(True))
                    return True
                else:
                    messagebox.showerror("Licença Desativada", "Licença desativada no servidor.")
                    if os.path.exists(LICENSE_FILE):
                        os.remove(LICENSE_FILE)
                    self.root.quit()
                    return False
                    
        except requests.exceptions.RequestException:
            if licenca_local_existente:
                self.log_message("Não foi possível verificar licença online. Tentando em 12h.", "warning")
                self.root.after(int(LICENSE_CHECK_INTERVAL * 1000), 
                              lambda: self.verificar_no_baserow(True))
                return True
            messagebox.showerror("Erro de Conexão", "Não foi possível conectar ao servidor de licenças.")
            return False
        return False

    def exibir_janela_ativacao(self):
        """Exibe janela de ativação."""
        self.ativacao_root = ctk.CTkToplevel(self.root)
        self.ativacao_root.title("Ativação Necessária")
        self.ativacao_root.geometry("300x150")
        self.ativacao_root.resizable(False, False)
        
        ctk.CTkLabel(self.ativacao_root, text="Software não ativado.").pack(pady=5)
        ctk.CTkLabel(self.ativacao_root, text="Código de ativação:").pack(pady=5)
        
        self.codigo_entry = ctk.CTkEntry(self.ativacao_root, width=250)
        self.codigo_entry.pack(pady=5)
        
        btn_ativar = ctk.CTkButton(self.ativacao_root, text="Ativar", command=self.ativar_software_online)
        btn_ativar.pack(pady=10)

        self.root.withdraw()

    def ativar_software_online(self):
        """Ativa software com código."""
        codigo_digitado = self.codigo_entry.get().strip()
        if not codigo_digitado:
            messagebox.showwarning("Campo Vazio", "Insira um código de ativação.")
            return
            
        try:
            headers = {"Authorization": f"Token {BASEROW_TOKEN}"}
            params = {"search": codigo_digitado}
            response = requests.get(
                f"{BASEROW_API_URL}{BASEROW_LICENSE_TABLE_ID}/?user_field_names=true", 
                headers=headers, params=params, timeout=10
            )
            response.raise_for_status()
            data = response.json()
            
            found_license = None
            for row in data.get("results", []):
                if row.get("chave") == codigo_digitado and row.get("ativa"):
                    found_license = row
                    break
            
            if found_license:
                data_expiracao_str = found_license.get("validade")
                if not data_expiracao_str:
                    messagebox.showerror("Erro", "Licença sem data de validade.")
                    return
                    
                hoje = datetime.now().date()
                data_expiracao = datetime.strptime(data_expiracao_str, "%Y-%m-%d").date()
                if hoje > data_expiracao:
                    messagebox.showerror("Licença Expirada", "A licença inserida já expirou.")
                    return
                    
                licenca_salvar = {
                    "data_expiracao": data_expiracao_str,
                    "ultima_verificacao_online": datetime.now().strftime("%Y-%m-%d %H:%M:%S")
                }
                with open(LICENSE_FILE, "w") as f:
                    json.dump(licenca_salvar, f, indent=2)

                messagebox.showinfo("Sucesso!", "Software ativado com sucesso!")
                self.ativacao_root.destroy()
                self.root.deiconify()
            else:
                messagebox.showerror("Erro", "Código inválido ou desativado.")
                
        except requests.exceptions.RequestException:
            messagebox.showerror("Erro de Conexão", "Não foi possível conectar ao servidor.")

    def carregar_dispositivos_audio(self):
        """Carrega dispositivos de áudio disponíveis."""
        self.device_list_output = ["Padrão do Sistema"]
        
        if SOUNDDEVICE_AVAILABLE:
            try:
                dispositivos = sd.query_devices()
                devices = [d['name'] for d in dispositivos if d['max_output_channels'] > 0]
                self.device_list_output.extend(devices)
            except:
                pass
        
        # Define dispositivo padrão
        saved_device = self.get_saved_audio_device()
        if saved_device and saved_device in self.device_list_output:
            self.saida_selecionada.set(saved_device)
        else:
            self.saida_selecionada.set(self.device_list_output[0])

    def selecionar_saida(self, novo_dispositivo_nome):
        """Seleciona dispositivo de saída."""
        self.log_message(f"Dispositivo selecionado: {novo_dispositivo_nome}", "info")
        if novo_dispositivo_nome != "Padrão do Sistema":
            self.log_message("⚠️ Para trocar dispositivo, altere nas configurações do Windows.", "warning")

    def iniciar_interface_principal(self):
        """Constrói a interface principal."""
        # Layout principal
        self.root.columnconfigure(0, weight=0)
        self.root.columnconfigure(1, weight=1)
        self.root.rowconfigure(0, weight=1)
        
        # Sidebar
        sidebar_frame = ctk.CTkFrame(self.root, width=250, corner_radius=0, fg_color="#2b2b2b")
        sidebar_frame.grid(row=0, column=0, sticky="nsew")
        
        # Título
        ctk.CTkLabel(sidebar_frame, text="ÁudioScheduler", 
                    font=("Arial", 20, "bold"), text_color="white").pack(pady=(15, 5))
        ctk.CTkLabel(sidebar_frame, text=f"v{VERSION}", 
                    font=("Arial", 10), text_color="#7a7a7a").pack()

        # Botões principais
        self.btn_add = ctk.CTkButton(sidebar_frame, text="Adicionar Áudio", command=self.adicionar_audio)
        self.btn_add.pack(fill="x", padx=15, pady=8)

        self.btn_test = ctk.CTkButton(sidebar_frame, text="🔊 Testar Áudio", command=self.testar_audio)
        self.btn_test.pack(fill="x", padx=15, pady=2)

        # Status
        status_card = ctk.CTkFrame(sidebar_frame, corner_radius=10)
        status_card.pack(fill="x", padx=15, pady=(8, 5))
        
        self.status_label = ctk.CTkLabel(status_card, text="Status: Parado", 
                                        font=("Arial", 16, "bold"), text_color="red")
        self.status_label.pack(pady=10)

        self.next_time_label = ctk.CTkLabel(status_card, text="Próxima: --:--:--", 
                                          font=("Arial", 18), text_color="#FF006A")
        self.next_time_label.pack()
        
        self.countdown_label = ctk.CTkLabel(status_card, text="", 
                                          font=("Arial", 45, "bold"), text_color="white")
        self.countdown_label.pack(pady=(0, 10))

        # Controles
        control_frame = ctk.CTkFrame(sidebar_frame, fg_color="transparent")
        control_frame.pack(fill="x", padx=15, pady=5)
        
        self.btn_iniciar = ctk.CTkButton(control_frame, text="Iniciar", command=self.iniciar_sequencia)
        self.btn_iniciar.pack(side="left", expand=True, fill="x", padx=5)
        
        self.btn_parar = ctk.CTkButton(control_frame, text="Parar", command=self.parar_sequencia)
        self.btn_parar.pack(side="right", expand=True, fill="x", padx=5)

        # Configurações
        config_frame = ctk.CTkFrame(sidebar_frame, fg_color="transparent")
        config_frame.pack(fill="x", padx=15, pady=8)

        # Agendamento
        ctk.CTkLabel(config_frame, text="Agendamento:").pack(anchor="w")
        self.agenda_checkbox = ctk.CTkCheckBox(config_frame, text="Ativar", 
                                             variable=self.agendamento_ativo)
        self.agenda_checkbox.pack(anchor="w", pady=(0, 5))
        
        ctk.CTkLabel(config_frame, text="Início (HH:MM):").pack(anchor="w")
        self.entry_start_time = ctk.CTkEntry(config_frame, placeholder_text="HH:MM")
        self.entry_start_time.pack(fill="x", pady=(0, 5))
        
        ctk.CTkLabel(config_frame, text="Parada (HH:MM):").pack(anchor="w")
        self.entry_stop_time = ctk.CTkEntry(config_frame, placeholder_text="HH:MM")
        self.entry_stop_time.pack(fill="x", pady=(0, 5))

        # Dispositivo
        ctk.CTkLabel(config_frame, text="Dispositivo:").pack(anchor="w", pady=(5, 0))
        self.carregar_dispositivos_audio()
        self.menu_saida = ctk.CTkOptionMenu(config_frame, values=self.device_list_output, 
                                          variable=self.saida_selecionada, 
                                          command=self.selecionar_saida)
        self.menu_saida.pack(fill="x")
        
        # Volume
        ctk.CTkLabel(config_frame, text="Volume:").pack(anchor="w", pady=(5,0))
        self.volume_slider = ctk.CTkSlider(config_frame, from_=0, to=1, 
                                         variable=self.volume_level, 
                                         command=self.ajustar_volume)
        self.volume_slider.pack(fill="x")
        
        self.alert_checkbox = ctk.CTkCheckBox(config_frame, text="Som de aviso", 
                                            variable=self.alert_enabled)
        self.alert_checkbox.pack(anchor="w", pady=10)

        # Área principal
        main_frame = ctk.CTkFrame(self.root, fg_color="#1e1e1e")
        main_frame.grid(row=0, column=1, sticky="nsew", padx=20, pady=20)
        
        ctk.CTkLabel(main_frame, text="Lista de Áudios", 
                    font=("Arial", 18, "bold"), text_color="white").pack(pady=10)
        
        # Lista scrollable
        self.list_frame = ctk.CTkScrollableFrame(main_frame, fg_color="transparent")
        self.list_frame.pack(fill="both", expand=True)
        
        # Log
        log_frame = ctk.CTkFrame(main_frame, fg_color="#1e1e1e")
        log_frame.pack(fill="x", pady=(10, 0), padx=20)
        
        ctk.CTkLabel(log_frame, text="Log de Atividades", 
                    font=("Arial", 14), text_color="#A0A0A0").pack(anchor="w")
        
        self.log_box = ctk.CTkTextbox(log_frame, height=100, corner_radius=10, fg_color="#2b2b2b")
        self.log_box.pack(fill="both", expand=True, pady=5)
        self.log_box.tag_config("info", foreground="white")
        self.log_box.tag_config("warning", foreground="orange")
        self.log_box.tag_config("error", foreground="red")
        self.log_box.configure(state="disabled")

        # Ícone
        self.load_icon()
        
        # Inicialização final
        self.carregar_lista()
        self.ajustar_volume(self.volume_level.get())
        self.atualizar_status()

    def load_icon(self):
        """Carrega o ícone da aplicação."""
        try:
            if os.path.exists("icon.ico"):
                icon_image = Image.open("icon.ico")
                icon_photo = ImageTk.PhotoImage(icon_image)
                self.root.iconphoto(True, icon_photo)
            else:
                self.log_message("Arquivo 'icon.ico' não encontrado.", "warning")
        except Exception as e:
            self.log_message(f"Erro ao carregar ícone: {e}", "warning")

    def ajustar_volume(self, value):
        """Ajusta o volume do sistema de áudio."""
        audio_manager.set_volume(float(value))

    def atualizar_lista_ui(self):
        """Atualiza a interface da lista de áudios."""
        # Remove frames antigos
        for frame in self.item_frames:
            if hasattr(frame, 'destroy'):
                try:
                    frame.destroy()
                except:
                    pass
        self.item_frames.clear()
        
        # Força limpeza de memória
        gc.collect()
        
        # Cria novos frames
        for i, item in enumerate(self.audio_items):
            try:
                frame = AudioItemFrame(self.list_frame, self, item, i)
                frame.pack(fill="x", pady=5)
                self.item_frames.append(frame)
            except Exception as e:
                self.log_message(f"Erro ao criar frame para {os.path.basename(item.path)}: {e}", "error")

    def move_audio(self, index, direction):
        """Move um áudio na lista."""
        new_index = index + direction
        if 0 <= new_index < len(self.audio_items):
            self.audio_items[index], self.audio_items[new_index] = \
                self.audio_items[new_index], self.audio_items[index]
            self.salvar_lista()
            self.atualizar_lista_ui()

    def testar_audio(self):
        """Testa o sistema de áudio."""
        self.log_message("Testando sistema de áudio...", "info")
        
        # Primeiro tenta tocar o som de alerta se existir
        if os.path.exists(ALERT_SOUND):
            success = audio_manager.play_sound(ALERT_SOUND, wait_finish=True)
            if success:
                self.log_message("✅ Teste de áudio concluído com sucesso!", "info")
                return
        
        # Se não tem arquivo de alerta, gera tom de teste
        try:
            self.gerar_tom_teste()
            self.log_message("✅ Tom de teste reproduzido com sucesso!", "info")
        except Exception as e:
            self.log_message(f"❌ Erro no teste de áudio: {e}", "error")

    def gerar_tom_teste(self):
        """Gera um tom de teste usando pygame."""
        try:
            import numpy as np
            
            # Parâmetros do tom
            duration = 0.5  # segundos
            sample_rate = 22050  # Usa taxa menor para compatibilidade
            frequency = 440  # Hz (Lá)
            
            # Gera onda senoidal
            frames = int(duration * sample_rate)
            arr = np.sin(2 * np.pi * frequency * np.linspace(0, duration, frames))
            
            # Converte para 16-bit
            arr = (arr * 32767 * 0.3).astype(np.int16)  # Volume reduzido
            
            # Cria array stereo
            stereo_arr = np.array([arr, arr]).T
            
            # Cria e toca o som
            sound = pygame.sndarray.make_sound(stereo_arr)
            sound.set_volume(audio_manager.current_volume)
            
            channel = sound.play()
            if channel:
                while channel.get_busy():
                    time.sleep(0.01)
            else:
                raise Exception("Não foi possível reproduzir o tom")
                
        except ImportError:
            raise Exception("NumPy não disponível para gerar tom de teste")

    def adicionar_audio(self):
        """Adiciona um novo áudio à lista."""
        self.log_message("Selecione um arquivo .WAV (sem acentos no nome).", "info")
        caminho = filedialog.askopenfilename(
            title="Selecione um áudio", 
            filetypes=[("Áudios WAV", "*.wav")]
        )
        if caminho:
            # Verifica se o arquivo é válido
            if not os.path.exists(caminho):
                self.log_message("Arquivo selecionado não existe.", "error")
                return
            
            intervalo = self.solicitar_intervalo()
            if intervalo is None:
                return
                
            item = AudioItem(caminho, intervalo)
            self.audio_items.append(item)
            self.salvar_lista()
            self.atualizar_lista_ui()
            self.log_message(f"Áudio '{os.path.basename(caminho)}' adicionado.", "info")

    def editar_audio(self, index):
        """Edita o intervalo de um áudio."""
        if index >= len(self.audio_items):
            return
            
        item = self.audio_items[index]
        novo_intervalo = simpledialog.askinteger(
            "Editar Intervalo",
            f"Novo intervalo para '{os.path.basename(item.path)}' (minutos):",
            initialvalue=item.interval,
            minvalue=1
        )
        
        if novo_intervalo is not None and novo_intervalo > 0:
            item.interval = novo_intervalo
            self.salvar_lista()
            self.atualizar_lista_ui()
            self.log_message(f"Intervalo de '{os.path.basename(item.path)}' alterado para {novo_intervalo} min.", "info")

    def solicitar_intervalo(self):
        """Solicita intervalo ao usuário."""
        while True:
            intervalo = simpledialog.askinteger(
                "Intervalo", 
                "Digite o intervalo em minutos:",
                minvalue=1
            )
            if intervalo is None:
                return None
            if intervalo > 0:
                return intervalo
            self.log_message("Digite um número maior que 0.", "warning")

    def remover_audio(self, index):
        """Remove um áudio da lista."""
        if index >= len(self.audio_items):
            return
            
        if self.current_index == index and self.running:
            self.log_message("Não é possível remover o áudio que está tocando.", "warning")
            return
            
        audio_removido = self.audio_items.pop(index)
        self.salvar_lista()
        self.atualizar_lista_ui()
        
        # Ajusta índice atual se necessário
        if self.current_index > index:
            self.current_index -= 1
            
        self.log_message(f"Áudio '{os.path.basename(audio_removido.path)}' removido.", "info")

    def iniciar_sequencia(self):
        """Inicia a sequência de reprodução."""
        # Verifica se há áudios válidos
        valid_audios = [item for item in self.audio_items if os.path.exists(item.path)]
        if not valid_audios:
            self.log_message("Adicione ao menos um áudio válido antes de iniciar.", "warning")
            return
            
        if self.running:
            self.log_message("A sequência já está em execução.", "warning")
            return
        
        # Limpa eventos de shutdown anteriores
        self.shutdown_event.clear()
        
        # Inicia reprodução
        self.running = True
        self.current_index = 0
        
        # Encontra o primeiro áudio válido
        while (self.current_index < len(self.audio_items) and 
               not os.path.exists(self.audio_items[self.current_index].path)):
            self.current_index += 1
            
        if self.current_index >= len(self.audio_items):
            self.current_index = 0
        
        # Inicia thread de reprodução
        self.play_thread = threading.Thread(target=self.tocar_sequencia, daemon=True)
        self.play_thread.start()
        
        self.log_message("Sequência de áudios iniciada.", "info")

    def tocar_sequencia(self):
        """Thread principal de reprodução de áudios."""
        while self.running and not self.shutdown_event.is_set():
            try:
                # Encontra próximo áudio válido
                attempts = 0
                max_attempts = len(self.audio_items)
                
                while attempts < max_attempts:
                    if self.current_index >= len(self.audio_items):
                        self.current_index = 0
                    
                    current_item = self.audio_items[self.current_index]
                    
                    if os.path.exists(current_item.path):
                        break
                    
                    self.log_message(f"Pulando '{os.path.basename(current_item.path)}' (não encontrado).", "warning")
                    self.current_index = (self.current_index + 1) % len(self.audio_items)
                    attempts += 1
                
                if attempts >= max_attempts:
                    self.log_message("Nenhum áudio válido encontrado.", "error")
                    break
                
                if self.shutdown_event.is_set():
                    break
                
                current_item = self.audio_items[self.current_index]
                
                # Define próximo tempo de execução
                next_delay = current_item.interval * 60
                self.next_execution_time = datetime.now() + timedelta(seconds=next_delay)
                
                # Toca alerta se habilitado
                if self.alert_enabled.get() and not self.shutdown_event.is_set():
                    if os.path.exists(ALERT_SOUND):
                        audio_manager.play_sound(ALERT_SOUND, wait_finish=True)
                
                # Toca áudio principal
                if not self.shutdown_event.is_set():
                    self.log_message(f"Reproduzindo: {os.path.basename(current_item.path)}", "info")
                    
                    success = audio_manager.play_sound(current_item.path, wait_finish=True)
                    if not success:
                        self.log_message(f"Erro ao reproduzir {os.path.basename(current_item.path)}", "error")
                
                # Cria timer preciso para o intervalo
                with self.timer_lock:
                    if self.current_timer:
                        self.current_timer.stop()
                    self.current_timer = PreciseTimer(next_delay)
                
                # Aguarda o intervalo
                while self.running and not self.shutdown_event.is_set():
                    remaining = self.current_timer.get_remaining()
                    if remaining <= 0:
                        break
                    
                    # Aguarda 100ms antes de verificar novamente
                    if self.shutdown_event.wait(0.1):
                        break
                
                # Próximo áudio
                self.current_index = (self.current_index + 1) % len(self.audio_items)
                
            except Exception as e:
                self.log_message(f"Erro na sequência: {e}", "error")
                time.sleep(1)  # Evita loop infinito em caso de erro
        
        # Cleanup da thread
        self.running = False
        self.current_index = -1
        self.next_execution_time = None
        
        with self.timer_lock:
            if self.current_timer:
                self.current_timer.stop()
                self.current_timer = None
        
        self.log_message("Sequência de áudios parada.", "info")

    def parar_sequencia(self):
        """Para a sequência de reprodução."""
        if not self.running:
            self.log_message("A sequência já está parada.", "warning")
            return
        
        self.log_message("Parando sequência...", "info")
        
        # Sinaliza para parar
        self.running = False
        self.shutdown_event.set()
        
        # Para todos os sons
        audio_manager.stop_all()
        
        # Para o timer atual
        with self.timer_lock:
            if self.current_timer:
                self.current_timer.stop()
                self.current_timer = None
        
        # Aguarda thread terminar (máximo 3 segundos)
        if self.play_thread and self.play_thread.is_alive():
            self.play_thread.join(timeout=3.0)

    def verificar_agendamento(self):
        """Verifica se deve iniciar/parar por agendamento."""
        if not self.agendamento_ativo.get():
            return
        
        try:
            now = datetime.now()
            current_time = now.strftime("%H:%M")
            
            start_str = self.entry_start_time.get().strip()
            stop_str = self.entry_stop_time.get().strip()
            
            # Verifica início
            if (start_str == current_time and 
                now.second < 30 and  # Evita múltiplas execuções no mesmo minuto
                not self.running):
                self.log_message("Horário de início atingido. Iniciando...", "info")
                self.iniciar_sequencia()
            
            # Verifica parada
            if (stop_str == current_time and 
                now.second < 30 and 
                self.running):
                self.log_message("Horário de parada atingido. Parando...", "info")
                self.parar_sequencia()
                
        except Exception:
            pass  # Ignora erros silenciosamente

    def get_saved_audio_device(self):
        """Obtém dispositivo salvo."""
        if os.path.exists(SAVE_FILE):
            try:
                with open(SAVE_FILE, "r", encoding="utf-8") as f:
                    data = json.load(f)
                if isinstance(data, dict):
                    return data.get("selected_device")
            except:
                pass
        return None

    def salvar_lista(self):
        """Salva a lista de áudios e configurações."""
        try:
            data = {
                "audios": [{"path": item.path, "interval": item.interval} 
                          for item in self.audio_items],
                "selected_device": self.saida_selecionada.get(),
                "volume": self.volume_level.get(),
                "alert_enabled": self.alert_enabled.get(),
                "agendamento_ativo": self.agendamento_ativo.get(),
                "start_time": self.entry_start_time.get() if hasattr(self, 'entry_start_time') else "",
                "stop_time": self.entry_stop_time.get() if hasattr(self, 'entry_stop_time') else ""
            }
            
            with open(SAVE_FILE, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2, ensure_ascii=False)
                
        except Exception as e:
            self.log_message(f"Erro ao salvar configurações: {e}", "error")

    def carregar_lista(self):
        """Carrega a lista de áudios e configurações."""
        if not os.path.exists(SAVE_FILE):
            return
        
        try:
            with open(SAVE_FILE, "r", encoding="utf-8") as f:
                data = json.load(f)
            
            # Compatibilidade com formato antigo (lista simples)
            if isinstance(data, list):
                for entry in data:
                    if isinstance(entry, dict) and "path" in entry and "interval" in entry:
                        item = AudioItem(entry["path"], entry["interval"])
                        self.audio_items.append(item)
            
            # Formato novo (dicionário com configurações)
            elif isinstance(data, dict):
                # Carrega áudios
                for entry in data.get("audios", []):
                    if "path" in entry and "interval" in entry:
                        item = AudioItem(entry["path"], entry["interval"])
                        self.audio_items.append(item)
                
                # Carrega configurações
                saved_device = data.get("selected_device")
                if saved_device and saved_device in self.device_list_output:
                    self.saida_selecionada.set(saved_device)
                
                self.volume_level.set(data.get("volume", 0.5))
                self.alert_enabled.set(data.get("alert_enabled", True))
                self.agendamento_ativo.set(data.get("agendamento_ativo", False))
                
                # Carrega horários de agendamento
                if hasattr(self, 'entry_start_time'):
                    self.entry_start_time.insert(0, data.get("start_time", ""))
                if hasattr(self, 'entry_stop_time'):
                    self.entry_stop_time.insert(0, data.get("stop_time", ""))
            
            self.atualizar_lista_ui()
            self.log_message(f"{len(self.audio_items)} áudios carregados.", "info")
            
        except Exception as e:
            self.log_message(f"Erro ao carregar configurações: {e}", "error")

    def atualizar_status(self):
        """Atualiza o status visual da aplicação."""
        try:
            # Status principal
            if self.running:
                self.status_label.configure(text="Status: Ligado", text_color="green")
            else:
                self.status_label.configure(text="Status: Parado", text_color="red")
            
            # Destaca áudio atual
            for i, frame in enumerate(self.item_frames):
                try:
                    if hasattr(frame, 'configure'):
                        if self.running and i == self.current_index:
                            frame.configure(fg_color="#366699")  # Azul para atual
                        else:
                            frame.configure(fg_color="#363636")  # Cinza padrão
                except:
                    pass
            
            # Contagem regressiva
            if self.running and self.next_execution_time:
                now = datetime.now()
                delta = self.next_execution_time - now
                remaining_seconds = max(0, int(delta.total_seconds()))
                
                if remaining_seconds > 0:
                    mins, secs = divmod(remaining_seconds, 60)
                    horario_str = self.next_execution_time.strftime("%H:%M:%S")
                    
                    self.next_time_label.configure(text=f"Próxima: {horario_str}")
                    self.countdown_label.configure(text=f"EM {mins:02d}:{secs:02d}")
                else:
                    self.next_time_label.configure(text="Próxima: AGORA")
                    self.countdown_label.configure(text="00:00")
            else:
                self.next_time_label.configure(text="Próxima: --:--:--")
                self.countdown_label.configure(text="")
            
            # Verifica agendamento
            self.verificar_agendamento()
            
        except Exception:
            pass  # Ignora erros de widgets destruídos
        
        # Agenda próxima atualização (sempre agenda, mesmo se houver erro)
        if not self.shutdown_event.is_set():
            self.root.after(1000, self.atualizar_status)

    def cleanup_resources(self):
        """Limpa recursos antes de fechar."""
        try:
            # Para tudo
            self.running = False
            self.shutdown_event.set()
            
            # Para mixer
            audio_manager.stop_all()
            
            # Para timer
            with self.timer_lock:
                if self.current_timer:
                    self.current_timer.stop()
                    self.current_timer = None
            
            # Limpa cache
            clear_sound_cache()
            
            # Para o pygame mixer
            pygame.mixer.quit()
            
            # Força garbage collection
            gc.collect()
            
        except Exception as e:
            print(f"Erro durante limpeza de recursos: {e}")

    def on_closing(self):
        """Manipula o fechamento da aplicação."""
        try:
            # Para sequência
            self.parar_sequencia()
            
            # Salva configurações
            self.salvar_lista()
            
            # Aguarda thread terminar
            if self.play_thread and self.play_thread.is_alive():
                self.play_thread.join(timeout=3.0)
            
            # Limpa recursos
            self.cleanup_resources()
            
        except Exception as e:
            print(f"Erro ao fechar aplicação: {e}")
        
        finally:
            self.root.destroy()


if __name__ == "__main__":
    try:
        root = ctk.CTk()
        app = AudioSchedulerApp(root)
        root.mainloop()
    except Exception as e:
        print(f"Erro crítico: {e}")
        input("Pressione Enter para fechar...")
    finally:
        # Garantia final de limpeza
        try:
            pygame.mixer.quit()
        except:
            pass