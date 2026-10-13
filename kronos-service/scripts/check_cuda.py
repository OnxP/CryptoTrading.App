import torch

print(torch.__version__)
print("CUDA:", torch.cuda.is_available())
print("Device:", torch.cuda.get_device_name(0) if torch.cuda.is_available() else "NONE")
print("VRAM GB:", torch.cuda.get_device_properties(0).total_memory / 1024**3 if torch.cuda.is_available() else 0)
