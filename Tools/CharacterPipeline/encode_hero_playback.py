"""CPU-only encode/decode validation of explicitly named completed hero captures."""
import argparse
import subprocess
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('captures', nargs='+', type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
blender = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
python = 'C:/Users/Mirim/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
for capture in args.captures:
    folder = (root / capture).resolve(strict=True)
    assert folder.is_relative_to(root / 'TestResults/CharacterPipeline/Motion')
    for script, options, log in [
        ('encode_motion.py', ['--sequence', str(folder), '--output', str(folder / 'motion.mp4')], 'encode.log'),
        ('verify_motion_video.py', ['--video', str(folder / 'motion.mp4')], 'decode.log')]:
        command = [blender, '--background', '--factory-startup', '--disable-autoexec', '--python-exit-code', '1',
                   '--python', str(root / 'Tools/CharacterPipeline' / script), '--', *options]
        with (folder / log).open('x', encoding='utf-8') as output:
            subprocess.run(command, cwd=root, stdout=output, stderr=subprocess.STDOUT, check=True)
    subprocess.run([python, str(root / 'Tools/CharacterPipeline/compare_motion_decode.py'), str(folder)], cwd=root, check=True)
    print(folder, 'VIDEO_ENCODE_AND_DECODE_VERIFIED', flush=True)
