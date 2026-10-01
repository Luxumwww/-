"""Convert supplied MP3s into the runtime's small PCM format; originals remain in 素材."""
from pathlib import Path
import sys, subprocess, shutil

root = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(root / "tools" / "_deps"))
import imageio_ffmpeg

tracks = {
    "meow": "喵喵叫.mp3",
    "hiss-1": "哈气/哈气1.mp3",
    "hiss-2": "哈气/哈气2.mp3",
    "hiss-3": "哈气/哈气3.mp3",
    "scared": "哈气/老吴.mp3",
    "snore": "打呼噜/cat-purr-3d-surround-sound.mp3",
    "satisfied": "舒服的低鸣/soothing-cat-purr.mp3",
}
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
for name, relative in tracks.items():
    source = root / "素材" / "音频" / relative
    output = root / "assets" / "audio" / f"{name}.wav"
    subprocess.run([ffmpeg, "-v", "error", "-y", "-i", str(source), "-ac", "1", "-ar", "22050", "-c:a", "pcm_s16le", str(output)], check=True)
    print(f"{name}: {output.stat().st_size:,} bytes")
for source in (root / "素材" / "图片").glob("*.png"):
    shutil.copy2(source, root / "assets" / "reference" / source.name)
