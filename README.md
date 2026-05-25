# 🎵 Audio Scheduler

O **AudioScheduler** é um automatizador e agendador de blocos de áudio desenvolvido em **C#** com interface gráfica (Windows Forms). Ele foi projetado especificamente para automação de rádio, permitindo reproduzir vinhetas, comerciais ou músicas de forma totalmente aleatória e em intervalos de tempo personalizados, enviando o som diretamente para dispositivos de áudio virtuais (como o Voicemeeter).

![Audio Scheduler](https://i.postimg.cc/G38fQ86T/Screenshot-40.png)

---

# Changelog - v1.0.1 📻

## 🚀 Novidades & Recursos
* **Desligamento Automático do Windows (Opcional):** Adicionada uma nova função nas configurações que permite forçar o desligamento do computador assim que o horário limite da automação for atingido. 
  * O comando conta com um delay de segurança de **1 minuto (60 segundos)** e fechamento forçado (`shutdown /s /f /t 60`), garantindo que o computador desligue sozinho e economize energia após a transmissão.

## 🛠️ Correções e Ajustes Finos

* **Estabilização dos Logs de Execução:** Substituído o método de escrita do histórico em segundo plano por um Invoke assíncrono seguro (`BeginInvoke`). Isso elimina completamente o congelamento, corte de textos e erros do tipo `Value cannot be null` ao processar eventos.
* **Painel de Horários Restaurado:** O botão **"Aplicar Horários"** foi reposicionado perfeitamente dentro do grupo de automação, permitindo o reinício correto e seguro da rádio ao atualizar o expediente.
* **Código Limpo e Otimizado:** Remoção completa de dependências e códigos experimentais de terceiros, mantendo o software leve, rápido e rodando de forma 100% nativa.

## ⚠️ Instruções de Atualização
Devido à reestruturação da tabela de configurações internas para o novo recurso de desligamento, é necessário **deletar o arquivo antigo `config_radio.db`** da pasta antes de iniciar esta nova versão pela primeira vez. O programa gerará um banco atualizado automaticamente.

---

## ✨ Funcionalidades

- ✅ Agendamento de áudios e propagandas  
- ✅ Definição de intervalos entre cada reprodução  
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
