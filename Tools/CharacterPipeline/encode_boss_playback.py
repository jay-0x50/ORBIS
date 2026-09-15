"""Serial CPU-only encoding and decode comparison of completed actual Unity captures."""
import argparse, subprocess
from pathlib import Path
parser=argparse.ArgumentParser();parser.add_argument('--labels',nargs='+',required=True);parser.add_argument('--run',default='Runtime01')
args=parser.parse_args();root=Path(__file__).resolve().parents[2]
blender='C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
python='C:/Users/Mirim/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
for name in args.labels:
    assert name in ['FireBoss','WaterBoss','RockBoss','WindBoss','LightningBoss']
    folder=root/'TestResults/CharacterPipeline/BossRuntimePlayback'/args.run/name
    for script,options,log in [
        ('encode_motion.py',['--sequence',str(folder),'--output',str(folder/'runtime.mp4'),'--boss'],'encode.log'),
        ('verify_motion_video.py',['--video',str(folder/'runtime.mp4')],'decode.log')]:
        command=[blender,'--background','--factory-startup','--disable-autoexec','--python-exit-code','1','--python',str(root/'Tools/CharacterPipeline'/script),'--',*options]
        with (folder/log).open('w',encoding='utf-8') as output:subprocess.run(command,cwd=root,stdout=output,stderr=subprocess.STDOUT,check=True)
    subprocess.run([python,str(root/'Tools/CharacterPipeline/compare_motion_decode.py'),str(folder),'--boss'],cwd=root,check=True)
    print(name,'VIDEO_ENCODE_AND_DECODE_VERIFIED',flush=True)
