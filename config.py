"""Configuração central do Suporte Valley."""
import os
from pathlib import Path
from dotenv import load_dotenv

load_dotenv()

TOKEN = os.getenv("DISCORD_TOKEN")
OPENAI_API_KEY = os.getenv("OPENAI_API_KEY", "").strip() or None
GROQ_API_KEY = os.getenv("GROQ_API_KEY", "").strip() or None
BOT_OWNER_ID = "1064174714400030720"  # ID do dono do bot
BASE_DIR = Path(__file__).parent
DATA_DIR = BASE_DIR / "data"
TRANSCRIPTS_DIR = BASE_DIR / "transcripts"

# Garante que as pastas existem
DATA_DIR.mkdir(exist_ok=True)
TRANSCRIPTS_DIR.mkdir(exist_ok=True)

GUILDS_FILE = DATA_DIR / "guilds.json"
TICKETS_FILE = DATA_DIR / "tickets.json"
