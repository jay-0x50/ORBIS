"""Read exact sRGB pixels from the user-supplied concept sheet; no raster edits.

Coordinates use the original 1086x1448 image's top-left origin. They were chosen
on visible cloth, brooch, gemstone, hair and skin surfaces, not inferred HEXes.
"""
from pathlib import Path
import hashlib, json
from PIL import Image

ROOT=Path(__file__).resolve().parents[2]
SOURCE=Path('C:/Users/Mirim/Downloads/여주인공.png')
POINTS=[('Navy',167,725,'large left cape midtone'),
        ('NavyShadow',701,786,'brooch inset navy cloth'),
        ('Ivory',443,376,'white bodice'),
        ('IvoryShadow',460,389,'bodice fold'),
        ('Gold',756,776,'brooch upper gold arm'),
        ('GoldShadow',727,828,'brooch shaded gold edge'),
        ('GoldHighlight',766,844,'brooch lit gold edge'),
        ('Turquoise',753,824,'brooch turquoise centre'),
        ('TurquoiseDeep',1006,866,'blade turquoise inset'),
        ('Skin',881,252,'portrait cheek'),
        ('Hair',849,87,'portrait ash-blond lock'),
        ('Leather',547,475,'dark belt glove leather'),
        ('Steel',1000,955,'blade dark steel')]
im=Image.open(SOURCE).convert('RGB')
assert im.size==(1086,1448)
data={'source':str(SOURCE),'sha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
      'width':im.width,'height':im.height,'colorSpace':'sRGB','sampling':'one exact pixel, original top-left coordinates','swatches':[]}
for name,x,y,label in POINTS:
    rgb=im.getpixel((x,y));data['swatches'].append(dict(name=name,x=x,y=y,label=label,r=rgb[0],g=rgb[1],b=rgb[2],hex='#'+''.join(f'{v:02X}' for v in rgb)))
out=ROOT/'Assets/Orbis/Game/LookDev/Resources/LookDev/ReferencePalette.json'
out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
doc=ROOT/'Docs/LookDev_Palette.md'
lines=['# 원화 직접 샘플링 팔레트','',f'원본: `{SOURCE}` · {im.width}×{im.height} · sRGB',f'SHA-256: `{data["sha256"]}`','',
       '원본 왼쪽 위를 (0,0)으로 삼아 실제 픽셀을 읽었습니다. 보간·평균·색 추정은 하지 않았습니다. Unity 셰이더 계산 시에는 sRGB→Linear 변환을 적용합니다.','',
       '| 이름 | 좌표 x,y | 직접 읽은 HEX | 원화 표면 |','|---|---|---|---|']
for c in data['swatches']:lines.append(f'| {c["name"]} | {c["x"]}, {c["y"]} | {c["hex"]} | {c["label"]} |')
doc.write_text('\n'.join(lines)+'\n',encoding='utf-8')
svg=['<svg xmlns="http://www.w3.org/2000/svg" width="920" height="660" viewBox="0 0 920 660">','<rect width="920" height="660" fill="#f7f5f0"/>',
     '<text x="28" y="34" font-family="sans-serif" font-size="22" fill="#252832">ORBIS — sampled concept palette</text>']
for i,c in enumerate(data['swatches']):
    x=28+(i%3)*300;y=60+(i//3)*118
    svg.extend([f'<rect x="{x}" y="{y}" width="270" height="68" rx="5" fill="{c["hex"]}"/>',
                f'<text x="{x}" y="{y+88}" font-family="sans-serif" font-size="15" fill="#252832">{c["name"]} {c["hex"]}</text>',
                f'<text x="{x}" y="{y+106}" font-family="sans-serif" font-size="12" fill="#555">pixel ({c["x"]}, {c["y"]})</text>'])
svg.append('</svg>');(ROOT/'Docs/LookDev_Palette.svg').write_text('\n'.join(svg),encoding='utf-8')
print(json.dumps(data,ensure_ascii=False))
