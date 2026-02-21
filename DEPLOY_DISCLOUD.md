# Deploy no Discloud via GitHub

## Persistência da configuração

Para manter canal de tickets, cargo de suporte etc. após cada deploy:

1. **Versionar `guilds.json`** — O arquivo `data/guilds.json` não está mais no .gitignore. Após configurar pelo `!sup`, faça:
   ```bash
   git add data/guilds.json
   git commit -m "Config do bot"
   git push
   ```

2. **Variável `DISCORD_DATA_DIR`** (opcional) — Se o Discloud oferecer volume persistente, defina no painel:
   ```
   DISCORD_DATA_DIR=/data
   ```
   E monte o volume em `/data`.

## Estrutura obrigatória do repositório

O **root do repositório** deve conter estes arquivos/pastas no mesmo nível:

```
├── run.py
├── main.py
├── config.py
├── discloud.config
├── requirements.txt
├── utils/
│   ├── __init__.py
│   ├── key_expiry.py
│   ├── storage.py
│   └── ...
├── cogs/
└── ...
```

## Conferência no GitHub

1. O repositório conectado ao Discloud deve ter `rusvalleysuporte` como raiz.
2. Verifique se a pasta `utils` está commitada:
   ```bash
   git status utils/
   git add utils/
   git commit -m "Incluir utils no deploy"
   ```

## Variáveis de ambiente no Discloud

Configure no painel do app:
- `DISCORD_TOKEN` — token do bot
- `OPENAI_API_KEY` (opcional)
- `GROQ_API_KEY` (opcional)
