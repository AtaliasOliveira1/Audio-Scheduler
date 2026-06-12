# 🎵 Audio Scheduler

O **AudioScheduler** é um automatizador e agendador de blocos de áudio desenvolvido em **C#** com interface gráfica (Windows Forms). Ele foi projetado especificamente para automação de rádio, permitindo reproduzir vinhetas, comerciais ou músicas de forma totalmente aleatória e em intervalos de tempo personalizados, enviando o som diretamente para dispositivos de áudio virtuais (como o Voicemeeter).

![Audio Scheduler](https://i.postimg.cc/hPgMvFFL/Screenshot-2.png)

---

# Changelog - v1.0.3 📻

Novidades v1.0.3:

    Adicionado função de Ducking (Baixa o som principal, para áudios disparados);
    Adicionado gatilho/botões para sons (3 slots);
    Volumes individuais para botões, voz, externo, ducking voz e ducking botões;
    Ajuste na UI/UX.


## ⚠️ Instruções de Instalação
Para que o banco local SQLite se ajuste perfeitamente às validações de inicialização da nova versão, lembre-se de **eliminar o arquivo `config_radio.db` antigo** da pasta antes de executar o novo programa pela primeira vez.

---

## ✨ Funcionalidades

- ✅ Agendamento de áudios e propagandas  
- ✅ Definição de intervalos entre cada reprodução
- ✅ Sistema de ducking
- ✅ Reprodução automática sem intervenção manual  
- ✅ Interface simples e prática (GUI opcional)  
- ✅ Feito para rádios web, podcasts e automação de mídia

---

## 🛠️ Tecnologias

 - Linguagem: C# (.NET 8.0)
 - Interface Gráfica: Windows Forms (Custom Dark Theme)
 - Manipulação de Áudio: NAudio (para controle de dispositivos de saída e reprodução de streams)
 - Banco de Dados: Microsoft.Data.Sqlite (armazenamento leve e local das configurações)
    
---

## 🚀 Como usar

1. Clone este repositório:  
```bash
git clone https://github.com/AtaliasOliveira1/audio-scheduler.git
