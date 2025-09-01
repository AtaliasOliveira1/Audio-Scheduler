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

pygame.mixer.init()

class AudioItem:
    def __init__(self, path, interval):
        self.path = path
        self.interval = interval  # minutos

class AudioSchedulerApp:
    def __init__(self, root):
        self.root = root
        self.root.title("ÁudioScheduler - by @ataliasloami v2.2")
        self.audio_items = []
        self.running = False
        self.play_thread = None
        self.current_index = -1
        self.alert_enabled = tk.BooleanVar(value=True)
        self.next_execution_time = None

        self.frame = tk.Frame(root)
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
        self.root.protocol("WM_DELETE_WINDOW", self.on_closing)
        self.atualizar_status()

    def tocar_wav(self, path):
        try:
            sound = pygame.mixer.Sound(path)
            channel = sound.play()
            while channel.get_busy():
                time.sleep(0.05)
        except Exception as e:
            print(f"Erro ao tocar {path}: {e}")

    def adicionar_audio(self):
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
        while True:
            intervalo = simpledialog.askinteger("Intervalo", "Digite o intervalo em minutos:")
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
            del self.audio_items[index]
            self.listbox.delete(index)
            self.salvar_lista()

    def iniciar_sequencia(self):
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
        if os.path.exists(ALERT_SOUND):
            self.tocar_wav(ALERT_SOUND)

    def parar_sequencia(self):
        if not self.running:
            messagebox.showinfo("Parado", "A sequência já está parada.")
            return
        self.running = False
        self.current_index = -1
        self.next_execution_time = None
        messagebox.showinfo("Parado", "Sequência de áudios parada.")

    def tocar_audio_individual(self, event):
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
        data = [{"path": item.path, "interval": item.interval} for item in self.audio_items]
        try:
            with open(SAVE_FILE, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2, ensure_ascii=False)
        except Exception as e:
            print("Erro ao salvar arquivo:", e)

    def carregar_lista(self):
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
        self.parar_sequencia()
        self.salvar_lista()
        self.root.destroy()


if __name__ == "__main__":
    root = tk.Tk()
    try:
        icon_img = ImageTk.PhotoImage(file="icon.png")
        root.iconphoto(True, icon_img)
    except Exception as e:
        print(f"Erro ao carregar o ícone PNG: {e}")

    root.geometry("380x460")
    root.resizable(False, False)
    app = AudioSchedulerApp(root)
    root.mainloop()
