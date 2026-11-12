"""Check that the 25H2+ byte patterns in lutdwm/dllmain.cpp each match exactly once in dwmcore.dll.

Run after a Windows update:  python check_aob.py [path\\to\\dwmcore.dll]
"""
import re
import sys
from pathlib import Path

dll = Path(sys.argv[1] if len(sys.argv) > 1 else r"C:\Windows\System32\dwmcore.dll").read_bytes()
src = (Path(__file__).parent / "lutdwm" / "dllmain.cpp").read_text()

ok = True
for name, body in re.findall(r"const unsigned char (\w+_w11_25h2)\[\] = \{(.*?)\};", src, re.S):
    tokens = [t.strip() for t in body.split(",") if t.strip()]
    rx = b"".join(b"." if t == "'?'" else re.escape(bytes([int(t, 16)])) for t in tokens)
    hits = [hex(m.start()) for m in re.finditer(rx, dll, re.S)]
    ok &= len(hits) == 1
    print(f"{'OK  ' if len(hits) == 1 else 'FAIL'} {name}: {hits}")
sys.exit(0 if ok else 1)
