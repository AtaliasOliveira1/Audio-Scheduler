import os
import json
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, simpledialog
from datetime import datetime, timedelta
import pygame
from PIL import Image, ImageTk
import time

SAVE_FILE = "audio_list.json"
ALERT_SOUND = "alert.wav"

# Inicializa o mixer do pygame (Deve ser feito apenas uma vez)
try:
    pygame.mixer.init()
except pygame.error as e:
    # Caso o mixer já tenha sido inicializado ou haja problema com drivers de áudio
    print(f"Aviso: Não foi possível inicializar pygame.mixer.init() novamente ou erro de áudio: {e}")


class AudioItem:
    def __init__(self, path, interval):
        self.path = path
        self.interval = interval  # minutos


class AudioSchedulerApp:
    def __init__(self, root):
        self.root = root
        self.root.title("ÁudioScheduler - by @ataliasloami v2.4")
        self.audio_items = []
        self.running = False
        self.play_thread = None
        self.current_index = -1
        self.next_execution_time = None
        self.last_trigger_time = None  # Para controle de agendamento por minuto
        
        # Variáveis de Configuração
        self.alert_enabled = tk.BooleanVar(value=True)
        self.scheduling_enabled = tk.BooleanVar(value=False)
        self.start_time_var = tk.StringVar(value="08:00")
        self.stop_time_var = tk.StringVar(value="18:00")
        
        # Variáveis internas para rastrear os horários *salvos*
        self.saved_start_time = self.start_time_var.get()
        self.saved_stop_time = self.stop_time_var.get()

        self.carregar_dados() # Carrega lista E configurações ANTES de montar a UI
        
        # --- Configura a Barra de Menu ---
        self.setup_menubar()

        self.frame = tk.Frame(root)
        self.frame.grid(padx=10, pady=10)

        # --- Lista de Áudios ---
        # row=0
        self.listbox = tk.Listbox(self.frame, width=60, height=10, selectmode=tk.SINGLE)
        self.listbox.grid(row=0, column=0, columnspan=2, sticky="nsew")
        self.listbox.bind("<Double-Button-1>", self.tocar_audio_individual)
        self.listbox.bind("<Button-3>", self.editar_intervalo_audio)
        self.repopular_listbox() # Repopula com os dados carregados
        
        # --------------------------------------------------------------------------------
        # 🆕 ALTERAÇÃO DE POSIÇÃO: Agendamento vem antes do Status
        # --------------------------------------------------------------------------------

        # --- Área de Agendamento (Novo: row=1) ---
        schedule_frame = tk.LabelFrame(self.frame, text="Agendamento Automático (HH:MM)", padx=5, pady=5)
        schedule_frame.grid(row=1, column=0, columnspan=2, sticky="ew", pady=10) 

        tk.Label(schedule_frame, text="Início:").grid(row=0, column=0)
        self.entry_start = tk.Entry(schedule_frame, textvariable=self.start_time_var, width=8, justify="center")
        self.entry_start.grid(row=0, column=1, padx=5)

        tk.Label(schedule_frame, text="Parada:").grid(row=0, column=2)
        self.entry_stop = tk.Entry(schedule_frame, textvariable=self.stop_time_var, width=8, justify="center")
        self.entry_stop.grid(row=0, column=3, padx=5)

        self.btn_aplicar = tk.Button(schedule_frame, text="Aplicar", command=self.aplicar_agendamento, width=6)
        self.btn_aplicar.grid(row=0, column=4, padx=5)

        self.schedule_checkbox = tk.Checkbutton(schedule_frame, text="Ativar Agendamento", variable=self.scheduling_enabled, fg="purple", font=("Arial", 9, "bold"))
        self.schedule_checkbox.grid(row=1, column=0, columnspan=5, pady=5) 


        # --- Status e Contagem (Novo: row=2, 3, 4, 5) ---
        self.status_label = tk.Label(self.frame, text="Status: Parado", fg="red", font=("Arial", 12, "bold"))
        self.status_label.grid(row=2, column=0, columnspan=2, pady=5) 

        self.next_time_label = tk.Label(self.frame, text="Próxima execução: --:--:--", font=("Arial", 10))
        self.next_time_label.grid(row=3, column=0, columnspan=2) 

        self.countdown_label = tk.Label(self.frame, text="", font=("Arial", 14, "bold"), fg="blue")
        self.countdown_label.grid(row=4, column=0, columnspan=2) 

        self.alert_checkbox = tk.Checkbutton(self.frame, text="Som de aviso antes de tocar", variable=self.alert_enabled)
        self.alert_checkbox.grid(row=5, column=0, columnspan=2, pady=2) 
        
        # --------------------------------------------------------------------------------
        # Fim da alteração de posição
        # --------------------------------------------------------------------------------

        # --- Configurações Finais ---
        self.frame.grid_rowconfigure(0, weight=1)
        self.frame.grid_columnconfigure((0, 1), weight=1)

        self.root.protocol("WM_DELETE_WINDOW", self.on_closing)
        self.atualizar_status()
        
    def setup_menubar(self):
        """Cria e configura a barra de menu padrão do Windows."""
        menubar = tk.Menu(self.root)
        self.root.config(menu=menubar)

        # Menu 1: Arquivos
        arquivo_menu = tk.Menu(menubar, tearoff=0)
        menubar.add_cascade(label="Arquivo", menu=arquivo_menu)
        arquivo_menu.add_command(label="Salvar Agora", command=self.salvar_dados)
        arquivo_menu.add_separator()
        arquivo_menu.add_command(label="Sair", command=self.on_closing)
        
        # Menu 2: Áudios
        audios_menu = tk.Menu(menubar, tearoff=0)
        menubar.add_cascade(label="Áudios", menu=audios_menu)
        audios_menu.add_command(label="Adicionar Áudios (WAV)...", command=self.adicionar_audio)
        audios_menu.add_command(label="Remover Selecionado", command=self.remover_audio)
        audios_menu.add_command(label="Editar Intervalo do Selecionado", command=self.editar_intervalo_menu)
        
        # Menu 3: Controle
        controle_menu = tk.Menu(menubar, tearoff=0)
        menubar.add_cascade(label="Controle", menu=controle_menu)
        controle_menu.add_command(label="▶️ Iniciar Sequência", command=self.iniciar_sequencia)
        controle_menu.add_command(label="⏹️ Parar Sequência", command=self.parar_sequencia)
        
    def aplicar_agendamento(self):
        """Salva os horários digitados nas variáveis de controle."""
        start = self.start_time_var.get()
        stop = self.stop_time_var.get()
        
        # Validação simples de formato HH:MM
        try:
            datetime.strptime(start, '%H:%M')
            datetime.strptime(stop, '%H:%M')
        except ValueError:
            messagebox.showerror("Erro de Formato", "O formato do horário deve ser HH:MM (ex: 08:00).")
            # Restaura os valores salvos
            self.start_time_var.set(self.saved_start_time)
            self.stop_time_var.set(self.saved_stop_time)
            return

        self.saved_start_time = start
        self.saved_stop_time = stop
        self.salvar_dados()
        messagebox.showinfo("Sucesso", f"Agendamento salvo: Início às {start}, Parada às {stop}.")

    def tocar_wav(self, path):
        try:
            if not pygame.mixer.get_init():
                pygame.mixer.init()
            
            sound = pygame.mixer.Sound(path)
            channel = sound.play()
            
            while channel.get_busy():
                time.sleep(0.05)
                
        except pygame.error as e:
            messagebox.showerror("Erro de Reprodução", f"Erro ao tocar {path}. Certifique-se de que o arquivo é um WAV válido e o mixer está funcionando. Erro: {e}")
        except Exception as e:
            print(f"Erro inesperado ao tocar {path}: {e}")

    def formatar_item_lista(self, item, em_execucao=False):
        """Formata a string de exibição para a listbox."""
        nome = os.path.basename(item.path)
        prefixo = "[▶️] " if em_execucao else ""
        
        return f"{prefixo}{nome} | {item.interval} min"

    def adicionar_audio(self):
        messagebox.showinfo("Atenção!", "O arquivo de áudio deve ser .WAV e não pode conter acentos ou caracteres especiais.")
        
        caminhos = filedialog.askopenfilenames(
            title="Selecione um ou mais áudios", 
            filetypes=[("Áudios WAV", "*.wav")]
        )
        
        if caminhos:
            intervalo = self.solicitar_intervalo()
            if intervalo is None:
                return

            for caminho in caminhos:
                item = AudioItem(caminho, intervalo)
                self.audio_items.append(item)
                self.listbox.insert(tk.END, self.formatar_item_lista(item))
                
            self.salvar_dados()
            messagebox.showinfo("Sucesso", f"{len(caminhos)} áudio(s) adicionado(s) com o intervalo de {intervalo} minutos.")

    def solicitar_intervalo(self):
        while True:
            intervalo = simpledialog.askinteger("Intervalo", "Digite o intervalo em minutos (para TODOS os áudios selecionados):")
            if intervalo is None:
                return None
            if intervalo > 0:
                return intervalo
            messagebox.showwarning("Valor inválido", "Digite um número inteiro maior que 0.")

    def remover_audio(self):
        selecionado = self.listbox.curselection()
        if selecionado:
            index = selecionado[0]
            if self.current_index == index and self.running:
                messagebox.showwarning("Aviso", "Não é possível remover o áudio que está tocando.")
                return
            
            if index < self.current_index and self.running:
                self.current_index -= 1
                
            del self.audio_items[index]
            self.listbox.delete(index)
            self.salvar_dados()

    def iniciar_sequencia(self, silent=False):
        if not self.audio_items:
            if not silent: messagebox.showwarning("Aviso", "Adicione ao menos um áudio antes de iniciar.")
            return
        if self.running:
            if not silent: messagebox.showinfo("Já está rodando", "A sequência já está em execução.")
            return
        
        self.running = True
        self.current_index = 0
        self.play_thread = threading.Thread(target=self.tocar_sequencia, daemon=True)
        self.play_thread.start()
        
        if not silent:
            messagebox.showinfo("Rodando", "Sequência de áudios iniciada.")

    def tocar_sequencia(self):
        while self.running:
            if not self.audio_items:
                self.running = False
                break

            if self.current_index >= len(self.audio_items):
                self.current_index = 0
                
            item = self.audio_items[self.current_index]
            
            if self.alert_enabled.get():
                self.tocar_alerta()
            
            self.tocar_wav(item.path)
            self.last_trigger_time = datetime.now().strftime("%H:%M") 

            start_time = time.monotonic()
            duration = item.interval * 60
            self.next_execution_time = datetime.now() + timedelta(seconds=duration)

            while self.running and time.monotonic() - start_time < duration:
                # Usa os horários *salvos* (saved_stop_time)
                if self.scheduling_enabled.get() and datetime.now().strftime("%H:%M") == self.saved_stop_time:
                    print("Parada automática detectada.")
                    self.running = False
                    break 
                time.sleep(0.5)

            if not self.running:
                break 

            if self.audio_items:
                self.current_index = (self.current_index + 1) % len(self.audio_items)
            else:
                self.current_index = 0

        self.current_index = -1
        self.next_execution_time = None
        self.root.after(0, lambda: self.atualizar_status()) 

    def tocar_alerta(self):
        if os.path.exists(ALERT_SOUND):
            self.tocar_wav(ALERT_SOUND)

    def parar_sequencia(self, silent=False):
        if not self.running:
            if not silent: messagebox.showinfo("Parado", "A sequência já está parada.")
            return
        self.running = False
        self.current_index = -1
        self.next_execution_time = None
        if not silent:
            messagebox.showinfo("Parado", "Sequência de áudios parada.")

    def tocar_audio_individual(self, event):
        selecionado = self.listbox.curselection()
        if selecionado:
            index = selecionado[0]
            if self.running:
                self.parar_sequencia(silent=True)
            self.current_index = index
            self.running = True
            self.play_thread = threading.Thread(target=self.tocar_sequencia, daemon=True)
            self.play_thread.start()
            
    def editar_intervalo_menu(self):
        """Função wrapper para chamar editar_intervalo_audio pelo menu sem o argumento event."""
        self.editar_intervalo_audio(None)

    def editar_intervalo_audio(self, event):
        selecionado = self.listbox.curselection()
        if selecionado:
            index = selecionado[0]
            novo_intervalo = self.solicitar_intervalo()
            if novo_intervalo is not None:
                self.audio_items[index].interval = novo_intervalo
                
                item_atualizado = self.audio_items[index]
                em_execucao = (self.running and self.current_index == index)
                
                self.listbox.delete(index)
                self.listbox.insert(index, self.formatar_item_lista(item_atualizado, em_execucao))
                
                self.salvar_dados()

    def salvar_dados(self):
        data = {
            "config": {
                "start_time": self.saved_start_time,
                "stop_time": self.saved_stop_time,
                "scheduling_enabled": self.scheduling_enabled.get(),
                "alert_enabled": self.alert_enabled.get()
            },
            "audios": [{"path": item.path, "interval": item.interval} for item in self.audio_items]
        }
        try:
            with open(SAVE_FILE, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2, ensure_ascii=False)
        except Exception as e:
            print("Erro ao salvar arquivo:", e)

    def carregar_dados(self):
        if os.path.exists(SAVE_FILE):
            try:
                with open(SAVE_FILE, "r", encoding="utf-8") as f:
                    data = json.load(f)

                if isinstance(data, dict):
                    config = data.get("config", {})
                    
                    self.saved_start_time = config.get("start_time", "08:00")
                    self.saved_stop_time = config.get("stop_time", "18:00")
                    self.start_time_var.set(self.saved_start_time)
                    self.stop_time_var.set(self.saved_stop_time)
                    
                    self.scheduling_enabled.set(config.get("scheduling_enabled", False))
                    self.alert_enabled.set(config.get("alert_enabled", True))
                    
                    audio_list = data.get("audios", [])
                else:
                    audio_list = data
                
                for entry in audio_list:
                    path = entry.get("path")
                    interval = entry.get("interval")
                    if path and interval:
                        item = AudioItem(path, interval)
                        self.audio_items.append(item)

            except Exception as e:
                print("Erro ao carregar arquivo:", e)
                
    def repopular_listbox(self):
        """Limpa e preenche a listbox com os dados carregados."""
        self.listbox.delete(0, tk.END)
        for item in self.audio_items:
            self.listbox.insert(tk.END, self.formatar_item_lista(item))

    def atualizar_status(self):
        current_time_str = datetime.now().strftime("%H:%M")

        # --- Lógica de Agendamento ---
        if self.scheduling_enabled.get():
            if current_time_str == self.saved_start_time:
                if not self.running and self.last_trigger_time != current_time_str:
                    print(f"Agendamento: Iniciando às {current_time_str}")
                    self.iniciar_sequencia(silent=True)
                    self.last_trigger_time = current_time_str
            
            elif current_time_str == self.saved_stop_time:
                if self.running and self.last_trigger_time != current_time_str:
                    print(f"Agendamento: Parando às {current_time_str}")
                    self.parar_sequencia(silent=True)
                    self.last_trigger_time = current_time_str
            
            if self.last_trigger_time and self.last_trigger_time != current_time_str:
                self.last_trigger_time = None

        # --- Atualização da UI (Lista de Áudios) ---
        for i, item in enumerate(self.audio_items):
            em_execucao = (i == self.current_index and self.running)
            texto_formatado = self.formatar_item_lista(item, em_execucao)
            
            if self.listbox.get(i) != texto_formatado:
                self.listbox.delete(i)
                self.listbox.insert(i, texto_formatado)
            
            if em_execucao:
                self.listbox.itemconfig(i, fg="green")
            else:
                self.listbox.itemconfig(i, fg="black")

        # --- Atualização de Status e Contagem ---
        if self.running:
            self.status_label.config(text="Status: Ligado", fg="green")
        else:
            self.status_label.config(text="Status: Parado", fg="red")

        if self.running and self.next_execution_time:
            restante = int((self.next_execution_time - datetime.now()).total_seconds())
            if restante < 0:
                restante = 0
            mins, secs = divmod(restante, 60)
            horario_str = self.next_execution_time.strftime("%H:%M:%S")
            self.next_time_label.config(text=f"Próxima execução: {horario_str}")
            self.countdown_label.config(text=f"Em {mins:02d}:{secs:02d}", fg="blue")
        else:
            self.next_time_label.config(text="Próxima execução: --:--:--")
            self.countdown_label.config(text="")

        self.root.after(1000, self.atualizar_status)

    def on_closing(self):
        #self.aplicar_agendamento() 
        self.parar_sequencia(silent=True)
        self.root.destroy()


if __name__ == "__main__":
    root = tk.Tk()
    try:
        # Tenta carregar ícone
        if os.path.exists("icon.png"):
            img = Image.open("icon.png")
            img = img.resize((32, 32), Image.LANCZOS)
            icon_img = ImageTk.PhotoImage(img)
            root.iconphoto(True, icon_img)
    except Exception as e:
        print(f"Erro ao carregar o ícone PNG. Instale 'Pillow' (pip install Pillow): {e}")

    # Ajustei o tamanho para 380x480 para a nova ordem, mas você pode refinar
    root.geometry("380x420")
    root.resizable(False, False)
    app = AudioSchedulerApp(root)
    root.mainloop()