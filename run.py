"""
Launcher para Discloud - define o path do projeto antes de importar main.
Necessário para garantir que o módulo 'utils' seja encontrado no container.
"""
import os
import sys

_script_dir = os.path.dirname(os.path.abspath(__file__))
os.chdir(_script_dir)

# Adiciona script_dir e parent ao path (funciona com deploy em subpasta)
for _p in [_script_dir, os.path.dirname(_script_dir)]:
    if _p and _p not in sys.path:
        sys.path.insert(0, _p)

# Verifica se utils existe
_utils_init = os.path.join(_script_dir, "utils", "__init__.py")
_parent_utils = os.path.join(os.path.dirname(_script_dir), "utils", "__init__.py")
if not os.path.isfile(_utils_init) and not os.path.isfile(_parent_utils):
    print("[ERRO] Pasta 'utils' não encontrada.")
    print("Diretório atual:", os.getcwd())
    print("Conteúdo:", os.listdir(_script_dir))
    sys.exit(1)

import main
main.main()
