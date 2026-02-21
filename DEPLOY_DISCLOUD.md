# Deploy do Rust Valley Bot Teste no Discloud (Discord.cloud)

## Configuração pronta

O projeto já está configurado para rodar na [Discloud](https://discloud.com) (Discord.cloud):

- `discloud.config` — configuração do bot
- `run.py` — ponto de entrada
- `requirements.txt` — dependências Python
- `.discloudignore` — arquivos não enviados no deploy

---

## Como fazer o deploy

### 1. Preparar o projeto

1. Crie um arquivo `.env` local com seu token (para testes locais) — **não** envie esse arquivo no zip.
2. Compacte a pasta `rustavlleybotteste1` em um arquivo `.zip`:
   - Inclua: `run.py`, `main.py`, `config.py`, `discloud.config`, `requirements.txt`, pastas `cogs/`, `utils/`, `data/`.
   - O `.discloudignore` exclui: `venv/`, `__pycache__/`, `.env`, `.git/`, etc.

### 2. Entrar no Discord da Discloud

1. Acesse: https://discord.discloudbot.com/
2. Entre no servidor.
3. Vá ao canal `#🔌・commands`.

### 3. Fazer o deploy

**Método avançado (com discloud.config):**

1. Execute o comando `.upconfig`
2. Envie o arquivo `.zip` quando solicitado
3. O bot será implantado usando o `discloud.config`

**Método rápido (sem discloud.config):**

1. Execute o comando `.up`
2. Informe:
   - **RAM:** `300` (em MB)
   - **Main file:** `run.py`
   - **Application ID:** ID do seu bot no [Discord Developer Portal](https://discord.com/developers/applications)
3. Envie o arquivo `.zip` quando solicitado

---

## Variáveis de ambiente (obrigatório)

Configure no painel da Discloud (Dashboard → seu app → variáveis):

| Variável        | Obrigatório | Descrição          |
|-----------------|-------------|--------------------|
| `DISCORD_TOKEN` | ✅ Sim      | Token do bot       |
| `OPENAI_API_KEY`| Não         | Para a IA          |
| `GROQ_API_KEY`  | Não         | Para a IA (Groq)   |

> O `.env` é ignorado no deploy por segurança. Use sempre as variáveis do painel da Discloud.

---

## Configuração atual (discloud.config)

```ini
NAME=RustValleyBotTeste
TYPE=bot
MAIN=run.py
RAM=300
VERSION=latest
BUILD=pip install -r requirements.txt
```

- **NAME:** nome do app na Discloud
- **TYPE:** bot Discord
- **MAIN:** arquivo de entrada
- **RAM:** 300 MB (mínimo 100 MB para bots)
- **VERSION:** Python latest
- **BUILD:** instalação das dependências antes de iniciar

---

## Dicas

1. **Dados persistentes:** O `data/guilds.json` fica no container. Para não perder configs, considere versioná-lo e incluir no zip.
2. **AUTORESTART:** Disponível apenas no plano Platinum. Removido da config para funcionar em todos os planos.
3. **Verificação de conta:** Se o sistema de verificação estiver indisponível, use a extensão do VS Code, o CLI ou o painel web da Discloud.
