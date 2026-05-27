# 🎵 Audio Scheduler

O **AudioScheduler** é um automatizador e agendador de blocos de áudio desenvolvido em **C#** com interface gráfica (Windows Forms). Ele foi projetado especificamente para automação de rádio, permitindo reproduzir vinhetas, comerciais ou músicas de forma totalmente aleatória e em intervalos de tempo personalizados, enviando o som diretamente para dispositivos de áudio virtuais (como o Voicemeeter).

![Audio Scheduler](https://i.postimg.cc/zvJdY6Mm/Screenshot-44.png)

---

# Changelog - v1.0.2 📻

## 🚀 Novidades & Melhorias Visuais
* **Contador de Áudios Centralizado:** Adicionado um indicador em tempo real mostrando a quantidade exata de arquivos válidos (`.mp3` e `.wav`) encontrados. O contador foi integrado diretamente ao painel **"MONITORAMENTO EM TEMPO REAL"** para melhorar a organização visual.
* **Leitura Perfeita no Dark Mode:** Corrigida a visibilidade do texto *"Ativar controle automático de horário"*, forçando a cor clara padrão do sistema para eliminar o fundo escuro que dificultava a leitura.
* **Fila Circular Aleatória Inteligente:** Substituído o sorteio puramente aleatório por um sistema baseado no algoritmo Fisher-Yates. Agora, o programa cria uma lista embaralhada com todas as vinhetas da pasta e toca uma por uma até o final, garantindo que **todos os áudios toquem sem repetição**. Ao chegar na última vinheta, a fila é reembaralhada automaticamente para reiniciar o loop.

## 🛠️ Correções de Comportamento
* **Sincronização em Tempo Real:** O sistema passou a monitorar a pasta de segundo em segundo. Caso adicione, renomeie ou remova qualquer áudio da pasta com o agendador ligado, a lista e o contador atualizam-se na hora sem travar a transmissão.
* **Coreção do Início Automático:** Corrigido o bug que iniciava o cronómetro sozinho mesmo com a automação por horário desmarcada. Agora, o programa carrega as configurações em Modo Manual e só liga de forma 100% automática no carregamento se o controle de horário estiver ativo no banco.
* **Restaurado o Histórico de Execuções:** Corrigido o envio de Logs para a interface. Agora, todas as ações do agendador automático e os acionamentos manuais da cartucheira registam o nome do arquivo e o horário exato do disparo na caixa de histórico.

## ⚠️ Instruções de Instalação
Para que o banco local SQLite se ajuste perfeitamente às validações de inicialização da nova versão, lembre-se de **eliminar o arquivo `config_radio.db` antigo** da pasta antes de executar o novo programa pela primeira vez.

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
