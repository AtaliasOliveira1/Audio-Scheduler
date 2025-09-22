import os
import json
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, simpledialog
from datetime import datetime, timedelta
import pygame
from PIL import Image, ImageTk
import time
import requests
import webbrowser

# ⚠️ INFORMAÇÕES DO PROGRAMA E CHAVES DE API ⚠️

# Mude esta versão a cada nova atualização que você lançar.
VERSION = "1.0" 

# Configurações do Baserow
BASEROW_API_URL = "https://api.baserow.io/api/database/rows/table/"
# --- PREENCHA SEUS DADOS AQUI ---
BASEROW_LICENSE_TABLE_ID = "682031"
BASEROW_UPDATE_TABLE_ID = "682052"
BASEROW_TOKEN = "dUragpDUHMQvaB9tmJu2a8Wzk09EIrnZ"
# -------------------------------

# Arquivos de configurações locais
SAVE_FILE = "audio_list.json"
ALERT_SOUND = "alert.wav"
LICENSE_FILE = "license.json"

# Inicia o mixer de áudio
pygame.mixer.init()

class AudioItem:
    """Representa um item de áudio na lista."""
    def __init__(self, path, interval):
        self.path = path
        self.interval = interval

class AudioSchedulerApp:
    """A classe principal da aplicação."""
    def __init__(self, root):
        self.root = root
        self.root.title(f"ÁudioScheduler - by @ataliasloami v{VERSION}")
        self.root.geometry("380x460")
        self.root.resizable(False, False)

        try:
            icon_img = ImageTk.PhotoImage(file="icon.png")
            self.root.iconphoto(True, icon_img)
        except Exception as e:
            print(f"Erro ao carregar o ícone PNG: {e}")

        self.audio_items = []
        self.running = False
        self.play_thread = None
        self.current_index = -1
        self.alert_enabled = tk.BooleanVar(value=True)
        self.next_execution_time = None
        
        # Inicia a verificação de licença
        if self.verificar_ativacao_hibrida():
            self.iniciar_interface_principal()
        else:
            self.exibir_janela_ativacao()

        self.root.protocol("WM_DELETE_WINDOW", self.on_closing)

    def verificar_atualizacao(self):
        """Verifica se há uma nova versão disponível no Baserow."""
        headers = {
            "Authorization": f"Token {BASEROW_TOKEN}",
            "Content-Type": "application/json"
        }
        
        try:
            response = requests.get(
                f"{BASEROW_API_URL}{BASEROW_UPDATE_TABLE_ID}/?user_field_names=true", 
                headers=headers
            )
            response.raise_for_status()
            data = response.json()
            
            ultima_versao_info = data.get("results", [])[0]
            versao_online = ultima_versao_info.get("versao")
            link_atualizacao = ultima_versao_info.get("link")
            
            if versao_online and link_atualizacao and float(versao_online) > float(VERSION):
                resposta = messagebox.askyesno(
                    "Nova Atualização Disponível!",
                    f"Uma nova versão ({versao_online}) está disponível. Você está usando a versão {VERSION}.\n\nDeseja abrir o link para baixar a atualização?"
                )
                if resposta:
                    webbrowser.open(link_atualizacao)
            
        except requests.exceptions.RequestException:
            pass # Ignora erros de conexão
        except (ValueError, IndexError, KeyError):
            pass # Ignora erros de formato dos dados

    def verificar_ativacao_hibrida(self):
        """Verifica a licença local e, se necessário, no Baserow."""
        licenca_valida_local = self.verificar_ativacao_local()
        
        if licenca_valida_local:
            try:
                with open(LICENSE_FILE, "r") as f:
                    licenca = json.load(f)
                
                ultima_verificacao_str = licenca.get("ultima_verificacao_online")
                if ultima_verificacao_str:
                    ultima_verificacao = datetime.strptime(ultima_verificacao_str, "%Y-%m-%d %H:%M:%S")
                    
                    if (datetime.now() - ultima_verificacao).total_seconds() < 24 * 3600:
                        return True
            except (IOError, json.JSONDecodeError, ValueError):
                pass
        
        return self.verificar_no_baserow(licenca_valida_local)

    def verificar_ativacao_local(self):
        """Verifica se a licença no arquivo local é válida."""
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
                messagebox.showerror("Licença Expirada", "Sua licença expirou. Por favor, entre em contato para renovação.")
                return False
            
            return True
        except (IOError, json.JSONDecodeError, ValueError):
            return False
            
    def verificar_no_baserow(self, licenca_local_existente=False):
        """Faz a verificação remota no Baserow."""
        headers = {
            "Authorization": f"Token {BASEROW_TOKEN}",
            "Content-Type": "application/json"
        }
        
        try:
            response = requests.get(
                f"{BASEROW_API_URL}{BASEROW_LICENSE_TABLE_ID}/?user_field_names=true", 
                headers=headers
            )
            response.raise_for_status()
            data = response.json()
            
            if licenca_local_existente:
                with open(LICENSE_FILE, "r") as f:
                    licenca_local = json.load(f)
                
                found_license = None
                for row in data.get("results", []):
                    if row.get("validade") == licenca_local.get("data_expiracao") and row.get("ativa"):
                        found_license = row
                        break
                
                if found_license:
                    licenca_local["ultima_verificacao_online"] = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
                    with open(LICENSE_FILE, "w") as f:
                        json.dump(licenca_local, f, indent=2)
                    return True
                else:
                    messagebox.showerror("Licença Desativada", "Sua licença foi desativada no servidor.")
                    os.remove(LICENSE_FILE)
                    return False
        except requests.exceptions.RequestException as e:
            if licenca_local_existente:
                messagebox.showwarning("Aviso de Conexão", "Não foi possível verificar a licença online, mas você pode continuar usando o programa.")
                return True
            messagebox.showerror("Erro de Conexão", f"Não foi possível conectar ao servidor de licenças. Verifique sua internet.")
            return False
        return False

    def exibir_janela_ativacao(self):
        """Cria a janela de ativação para o usuário."""
        self.ativacao_root = tk.Toplevel(self.root)
        self.ativacao_root.title("Ativação Necessária")
        self.ativacao_root.geometry("300x150")
        self.ativacao_root.resizable(False, False)
        
        tk.Label(self.ativacao_root, text="Este software não está ativado.").pack(pady=5)
        tk.Label(self.ativacao_root, text="Insira seu código de ativação:").pack(pady=5)
        
        self.codigo_entry = tk.Entry(self.ativacao_root, width=40)
        self.codigo_entry.pack(pady=5)
        
        btn_ativar = tk.Button(self.ativacao_root, text="Ativar", command=self.ativar_software_online)
        btn_ativar.pack(pady=10)

        self.root.withdraw()

    def ativar_software_online(self):
        """Processa a ativação, consultando o Baserow."""
        codigo_digitado = self.codigo_entry.get().strip()
        if not codigo_digitado:
            messagebox.showwarning("Campo Vazio", "Por favor, insira um código de ativação.")
            return

        headers = {
            "Authorization": f"Token {BASEROW_TOKEN}",
            "Content-Type": "application/json"
        }
        
        params = {
            "search": codigo_digitado,
        }
        
        try:
            response = requests.get(
                f"{BASEROW_API_URL}{BASEROW_LICENSE_TABLE_ID}/?user_field_names=true", 
                headers=headers,
                params=params
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
                    messagebox.showerror("Erro de Ativação", "Licença inválida ou sem data de validade.")
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
                self.iniciar_interface_principal()
            else:
                messagebox.showerror("Erro de Ativação", "Código de ativação inválido ou desativado.")
        
        except requests.exceptions.RequestException:
            messagebox.showerror("Erro de Conexão", f"Não foi possível conectar ao servidor de licenças. Verifique sua internet.")
        except (ValueError, KeyError):
            messagebox.showerror("Erro de Ativação", f"Resposta inválida do servidor.")

    def iniciar_interface_principal(self):
        """Constrói a interface principal do aplicativo e checa por atualizações."""
        self.frame = tk.Frame(self.root)
        self.frame.grid(padx=10, pady=10)

        self.listbox = tk.Listbox(self.frame, width=60, height=10)
        self.listbox.grid(row=0, column=0, columnspan=2, sticky="nsew")
        self.listbox.bind("<Double-Button-1>", self.tocar_audio_individual)
        self.listbox.bind("<Button-3>", self.editar_intervalo_audio)

        self.btn_add = tk.Button(self.frame, text="Adicionar Áudio", command=self.adicionar_audio)
        self.btn_add.grid(row=1, column=0, sticky="ew", pady=5)

        self.btn_remover = tk.Button(self.frame, text="Remover Selecionado", command=self.remover_audio)
        self.btn_remover.grid(row=1, column=1, sticky="ew", pady=5)

        self.btn_iniciar = tk.Button(self.frame, text="Iniciar", command=self.iniciar_sequencia)
        self.btn_iniciar.grid(row=2, column=0, sticky="ew", pady=5)

        self.btn_parar = tk.Button(self.frame, text="Parar", command=self.parar_sequencia)
        self.btn_parar.grid(row=2, column=1, sticky="ew", pady=5)

        self.status_label = tk.Label(self.frame, text="Status: Parado", fg="red", font=("Arial", 12, "bold"))
        self.status_label.grid(row=3, column=0, columnspan=2, pady=5)

        self.next_time_label = tk.Label(self.frame, text="Próxima execução: --:--:--", font=("Arial", 10))
        self.next_time_label.grid(row=4, column=0, columnspan=2)

        self.countdown_label = tk.Label(self.frame, text="", font=("Arial", 14, "bold"), fg="blue")
        self.countdown_label.grid(row=5, column=0, columnspan=2)

        self.alert_checkbox = tk.Checkbutton(self.frame, text="Som de aviso antes de tocar", variable=self.alert_enabled)
        self.alert_checkbox.grid(row=6, column=0, columnspan=2, pady=5)

        self.frame.grid_rowconfigure(0, weight=1)
        self.frame.grid_columnconfigure((0, 1), weight=1)

        self.carregar_lista()
        self.atualizar_status()
        
        self.verificar_atualizacao()

    def tocar_wav(self, path):
        """Toca um arquivo .wav e aguarda o término."""
        try:
            sound = pygame.mixer.Sound(path)
            channel = sound.play()
            while channel.get_busy():
                time.sleep(0.05)
        except Exception as e:
            print(f"Erro ao tocar {path}: {e}")

    def adicionar_audio(self):
        """Abre uma caixa de diálogo para adicionar um áudio à lista."""
        messagebox.showinfo("Atenção!", "O arquivo de áudio deve ser .WAV e não pode conter acentos!")
        caminho = filedialog.askopenfilename(title="Selecione um áudio", filetypes=[("Áudios WAV", "*.wav")])
        if caminho:
            intervalo = self.solicitar_intervalo()
            if intervalo is None:
                return
            item = AudioItem(caminho, intervalo)
            self.audio_items.append(item)
            nome = os.path.basename(caminho)
            self.listbox.insert(tk.END, f"{nome} - a cada {intervalo} min")
            self.salvar_lista()

    def solicitar_intervalo(self):
        """Pede ao usuário um intervalo de tempo em minutos."""
        while True:
            intervalo = simpledialog.askinteger("Intervalo", "Digite o intervalo em minutos:")
            if intervalo is None:
                return None
            if intervalo > 0:
                return intervalo
            messagebox.showwarning("Valor inválido", "Digite um número inteiro maior que 0.")

    def remover_audio(self):
        """Remove o áudio selecionado da lista."""
        selecionado = self.listbox.curselection()
        if selecionado:
            index = selecionado[0]
            if self.current_index == index and self.running:
                messagebox.showwarning("Aviso", "Não é possível remover o áudio que está tocando.")
                return
            del self.audio_items[index]
            self.listbox.delete(index)
            self.salvar_lista()

    def iniciar_sequencia(self):
        """Inicia a reprodução da sequência de áudios."""
        if not self.audio_items:
            messagebox.showwarning("Aviso", "Adicione ao menos um áudio antes de iniciar.")
            return
        if self.running:
            messagebox.showinfo("Já está rodando", "A sequência já está em execução.")
            return
        self.running = True
        self.current_index = 0
        self.play_thread = threading.Thread(target=self.tocar_sequencia, daemon=True)
        self.play_thread.start()
        messagebox.showinfo("Rodando", "Sequência de áudios iniciada.")

    def tocar_sequencia(self):
        """Loop principal para tocar os áudios em sequência."""
        while self.running:
            item = self.audio_items[self.current_index]
            self.atualizar_status()
            if self.alert_enabled.get():
                self.tocar_alerta()
            self.tocar_wav(item.path)

            start_time = time.monotonic()
            duration = item.interval * 60
            self.next_execution_time = datetime.now() + timedelta(seconds=duration)

            while self.running and time.monotonic() - start_time < duration:
                time.sleep(0.5)

            self.current_index = (self.current_index + 1) % len(self.audio_items)

        self.current_index = -1
        self.next_execution_time = None

    def tocar_alerta(self):
        """Toca o som de alerta."""
        if os.path.exists(ALERT_SOUND):
            self.tocar_wav(ALERT_SOUND)

    def parar_sequencia(self):
        """Para a sequência de reprodução."""
        if not self.running:
            messagebox.showinfo("Parado", "A sequência já está parada.")
            return
        self.running = False
        self.current_index = -1
        self.next_execution_time = None
        messagebox.showinfo("Parado", "Sequência de áudios parada.")

    def tocar_audio_individual(self, event):
        """Toca um áudio individualmente ao dar um duplo clique."""
        selecionado = self.listbox.curselection()
        if selecionado:
            index = selecionado[0]
            if self.running:
                self.parar_sequencia()
            self.current_index = index
            self.running = True
            self.play_thread = threading.Thread(target=self.tocar_sequencia, daemon=True)
            self.play_thread.start()

    def editar_intervalo_audio(self, event):
        """Edita o intervalo de um áudio selecionado."""
        selecionado = self.listbox.curselection()
        if selecionado:
            index = selecionado[0]
            novo_intervalo = self.solicitar_intervalo()
            if novo_intervalo is not None:
                self.audio_items[index].interval = novo_intervalo
                nome = os.path.basename(self.audio_items[index].path)
                self.listbox.delete(index)
                self.listbox.insert(index, f"{nome} - a cada {novo_intervalo} min")
                self.salvar_lista()

    def salvar_lista(self):
        """Salva a lista de áudios em um arquivo JSON."""
        data = [{"path": item.path, "interval": item.interval} for item in self.audio_items]
        try:
            with open(SAVE_FILE, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2, ensure_ascii=False)
        except Exception as e:
            print("Erro ao salvar arquivo:", e)

    def carregar_lista(self):
        """Carrega a lista de áudios de um arquivo JSON."""
        if os.path.exists(SAVE_FILE):
            try:
                with open(SAVE_FILE, "r", encoding="utf-8") as f:
                    data = json.load(f)
                for entry in data:
                    path = entry.get("path")
                    interval = entry.get("interval")
                    if path and interval:
                        item = AudioItem(path, interval)
                        self.audio_items.append(item)
                        nome = os.path.basename(path)
                        self.listbox.insert(tk.END, f"{nome} - a cada {interval} min")
            except Exception as e:
                print("Erro ao carregar arquivo:", e)

    def atualizar_status(self):
        """Atualiza o status e o contador na interface."""
        for i in range(self.listbox.size()):
            texto = self.listbox.get(i)
            if i == self.current_index and self.running:
                if not texto.startswith("[▶️] "):
                    nome = os.path.basename(self.audio_items[i].path)
                    self.listbox.delete(i)
                    self.listbox.insert(i, f"[▶️] {nome} - a cada {self.audio_items[i].interval} min")
                self.listbox.itemconfig(i, fg="green")
            else:
                if texto.startswith("[▶️] "):
                    nome = os.path.basename(self.audio_items[i].path)
                    self.listbox.delete(i)
                    self.listbox.insert(i, f"{nome} - a cada {self.audio_items[i].interval} min")
                self.listbox.itemconfig(i, fg="black")

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
        """Lida com o fechamento da janela."""
        self.parar_sequencia()
        self.salvar_lista()
        self.root.destroy()

if __name__ == "__main__":
    root = tk.Tk()
    app = AudioSchedulerApp(root)
    root.mainloop()