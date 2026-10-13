"""ENV-007: resolve Kronos-base and tokenizer (from HF cache when present) and print identity."""
import hashlib
import sys
from pathlib import Path

from huggingface_hub import snapshot_download

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "third_party" / "Kronos"))

from model import Kronos, KronosTokenizer  # noqa: E402

for repo_id in ("NeoQuasar/Kronos-Tokenizer-base", "NeoQuasar/Kronos-base"):
    path = Path(snapshot_download(repo_id))
    weights = path / "model.safetensors"
    sha = hashlib.sha256(weights.read_bytes()).hexdigest()
    print(f"{repo_id}: revision={path.name} bytes={weights.stat().st_size} sha256={sha}")

tokenizer = KronosTokenizer.from_pretrained("NeoQuasar/Kronos-Tokenizer-base")
model = Kronos.from_pretrained("NeoQuasar/Kronos-base")
print("params M:", sum(p.numel() for p in model.parameters()) / 1e6)
print("Kronos-base and tokenizer are available.")
