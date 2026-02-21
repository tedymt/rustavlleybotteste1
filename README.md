# Suporte Valley

Bot de tickets para Discord em Python, focado em suporte multilíngue (PT/EN).  
**Armazenamento 100% em JSON** — sem banco de dados.

## Características

- **JSON puro** — `data/guilds.json`, `data/tickets.json`, `transcripts/*.json`
- **Tradução automática** — mensagens salvas em PT e EN nos transcripts
- **Departamentos** — configuráveis (Suporte, Vendas, etc.)
- **Transcript em JSON** — fácil de buscar e processar

## Instalação

```bash
cd suporte_valley
pip install -r requirements.txt
```

Crie um arquivo `.env`:

```
DISCORD_TOKEN=seu_token_do_bot
```

## Executar

```bash
python main.py
```

Ou:

```bash
py main.py
```

## Comando Principal

| Comando | Descrição |
|---------|-----------|
| `!sup` | Abre o painel central com menu de categorias (Ticket \| Config Bot) |

## Estrutura

```
suporte_valley/
├── main.py           # Entry point
├── config.py
├── requirements.txt
├── data/
│   ├── guilds.json   # Config por servidor
│   └── tickets.json  # Tickets ativos + histórico
├── transcripts/      # Transcripts fechados (JSON)
├── cogs/
│   └── tickets.py
└── utils/
    ├── storage.py    # Leitura/escrita JSON
    └── translator.py # Tradução PT/EN
```

## Configuração inicial

1. Adicione o bot ao servidor (Permissão: Administrador)
2. Edite `config.py` e defina seu ID em `BOT_OWNER_ID`
3. Digite `!sup` no Discord
4. Selecione **Ticket** → configure categoria, logs, cargo de suporte
5. Clique em **Publicar Painel** no canal desejado
6. (Opcional) Selecione **Config Bot** → adicione outros usuários autorizados

## Transcript JSON

Ao fechar um ticket, é gerado em `transcripts/` algo como:

```json
{
  "ticket": {
    "ticket_code": "SV-A3F9",
    "author_id": "...",
    "staff_id": "...",
    "created_at": "...",
    "closed_at": "..."
  },
  "messages": [
    {
      "author_name": "Usuario",
      "content": "Preciso de ajuda",
      "translations": {
        "original": "Preciso de ajuda",
        "pt": "Preciso de ajuda",
        "en": "I need help"
      },
      "timestamp": "..."
    }
  ]
}
```
