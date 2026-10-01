from pathlib import Path
import subprocess, sys
root=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(root/'tools'/'_deps'))
import imageio_ffmpeg
base=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else root/'artifacts'/'preview-v2-final'
for name in ['walk','mouth','scared','post-chew','eat']:
    subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(),'-v','error','-y','-framerate','6' if name=='walk' else '8','-i',str(base/name/'%02d.png'),
        '-filter_complex','[0:v]split[a][b];[a]palettegen=reserve_transparent=1[p];[b][p]paletteuse=alpha_threshold=128:dither=sierra2_4a',
        '-loop','0',str(base/(name+'.gif'))],check=True)
    print(name+'.gif')
