# 의상 알베도 텍스처 생성 기록

2026-09-11. imagegen 스킬을 읽고 내장 `image_gen.imagegen` 도구로 각각 한 장씩 생성했다. CLI/API 우회, 생성 후 크기 변경, 픽셀 보정은 사용하지 않았다. 원화는 의상 색·천결·금색 천체 문양의 참고 이미지로만 제공했다.

두 장 모두 2048×2048을 프롬프트에 요청했으나 실제 반환 파일은 **1254×1254**다. 파일을 확대하지 않고 원본 그대로 프로젝트에 복사했다. Unity의 maxTextureSize는 2048, NPOT Scale은 None이므로 실제 원본 해상도를 유지한다.

| 재질 역할 | 파일 | 실제 크기 | 매핑 |
|---|---|---|---|
| EX_TailoredIvory | Assets/Orbis/Game/Island/Textures/Explorers/Body_Ivory_BaseColor.png | 1254×1254 | 흰 상의·소매·스커트 패널의 전체 UV 0..1 |
| EX_TailoredNavy | Assets/Orbis/Game/Island/Textures/Explorers/Body_Navy_BaseColor.png | 1254×1254 | 남색 옷·망토 패널의 전체 UV 0..1; 문양 중심 U≈0.5, V≈0.25 |

## 원본과 해시

- `Assets/Img/여주인공.png`: 1086×1448, 2245562 bytes, SHA-256 `C352B2B9E898D090C257A451734B5685D30FE7E0E8B60444B6737FB6FB8DE4F7`.
- `Assets/Img/남주인공.png`: 1086×1448, 2365946 bytes, SHA-256 `B551F067DAAECBFA91002EB3D04C8C4BD2F64E7EA36C2E6BAD6EE2AD03632FA4`.
- `Assets/Orbis/Game/Island/Textures/Explorers/Body_Ivory_BaseColor.png`: 1254×1254, 2509023 bytes, SHA-256 `E3A52E2E126072CC5493ABD7DA92A4E7F7CFAF0418F36DD27C803163EB89B6FD`.
- `Assets/Orbis/Game/Island/Textures/Explorers/Body_Navy_BaseColor.png`: 1254×1254, 2555129 bytes, SHA-256 `C3522B6B361453D83CD2E1E5B2340D37C5F319050B506F6E4CD2F00BF3073D57`.

내장 도구의 원본 생성 파일(삭제·수정하지 않음):

- Ivory: `C:\Users\Mirim\.codex\generated_images\01a08e9a-763d-76d1-bb5f-0dcf2572a88d\exec-0a102d1f-0ef4-4df5-85b7-c858dda17c32.png`
- Navy: `C:\Users\Mirim\.codex\generated_images\01a08e9a-763d-76d1-bb5f-0dcf2572a88d\exec-17ea0422-af61-400b-a110-4e4c209e5533.png`

## Unity 연결

`ExplorerArtImport.Build()`가 기존 얼굴 2장·머리카락 1장에 더해 이 의상 2장을 필수로 임포트한다. 각 주인공의 EX_TailoredIvory와 EX_TailoredNavy 재질을 명시적으로 매핑하며 누락하면 검증 오류를 낸다. BaseColor white, UV scale(1,1)/offset(0,0), sRGB, Trilinear, mipmap, Clamp, Uncompressed, Read/Write off 설정을 사용한다. 새 옷 재질에만 부드러운 명암과 0.35px 외곽선을 적용한다. 기존 얼굴·머리·피부 재질 계약과 다른 동료·환경 머티리얼은 유지한다.

PNG의 실제 패널 구성을 검수했다. Ivory는 빈 공간을 충분히 둔 잔잔한 금색 별 자수와 세로 천결, Navy는 윗부분의 단색 천결과 아래쪽의 나침반·가는 금색 헴으로 구성되어 있다. 별도 인물·글자·배경 여백은 없다. 메시 조형과 실제 Unity 조명 검증은 통합 단계에서 수행한다.

원화의 권리와 출처는 기존 Credits 및 사용자 제공 원화의 조건을 따른다. 이 기록은 원화에 새 라이선스를 부여하지 않는다.

## 사용 프롬프트 — Ivory

```text
Use case: stylized-concept.
Asset type: production-ready color/albedo texture for the shared ivory cloth on two original anime fantasy RPG protagonist 3D models.
Input images: Image 1 and Image 2 are reference images ONLY for the ivory costume fabric, fine gold embroidery, elegant celestial details and muted luxurious palette. Do not render the characters or copy the layout of the reference sheets.
Primary request: generate ONE square 2048 x 2048 pixel, full-bleed, opaque, orthographic flat fabric albedo texture. The entire image must be warm pearl ivory woven cloth, with exceptionally subtle long vertical fabric grain and low-contrast soft vertical tailoring folds. Use sparse delicate pale-gold celestial seam embroidery and a few tiny understated four-point stars; keep most of the surface uncluttered to map cleanly to curved torso, sleeves and skirt panels. Fine silk/cotton surface with tasteful hand-painted anime material finish and exquisite detail visible at close range.
Composition: full rectangular cloth surface extends completely to every edge. Overall base ivory near sRGB #EDE7DD, fine embroidery near #BCA47C; gentle cool ivory variation as in the references. Vertical direction runs continuously from top to bottom, with no horizontal breaks. Very low contrast fabric shading, no baked directional light, because Unity will supply lighting. Sparse embroidery must not form a rigid picture frame, large central emblem or big stripes.
Constraints: no character, face, hands, body, mannequin, garment outline, background margin, text, letters, logo, watermark, texture grid, seams chart, collage or UV wireframe. No metal armor, hard cast shadows, bright glow, raised gold plaque, large rim/frame, dramatic drapery or perspective. Opaque RGB albedo image, not a transparency cutout, not a cloth photograph displayed on a white background. Return the texture itself.
```

## 사용 프롬프트 — Navy

```text
Use case: stylized-concept.
Asset type: production-ready color/albedo texture for an original anime fantasy RPG protagonist's navy coat and cape panel.
Input images: Image 1 and Image 2 are reference images ONLY for the navy-blue fabric color, refined golden compass/star embroidery and celestial costume visual language. Do not render the characters or the reference sheet layout.
Primary request: generate ONE square 2048 x 2048 pixel, full-bleed, opaque, straight-on flat cloth albedo texture, suitable to map UV 0..1 onto one smooth long cape panel. Use the elegant subdued slate-navy of the reference capes, with fine vertical silk-wool weave and extremely low-contrast long vertical cloth variation. The upper two thirds must be mostly plain clean navy cloth. In the lower third, centered horizontally, place one refined thin pale antique-gold compass rose / celestial navigation emblem inspired by the references, with a few tiny spaced four-point star accents. Near the bottom edge and along the lower sides, a narrow delicate golden stitched hem line; the upper edge remains plain. Embroidery lies flat within fabric, never raised armor.
Composition: cloth fills every pixel all the way to every edge; no empty background or garment silhouette. The design reads as one complete continuous elegant rectangular cloth panel, no tile grid. Base color close to sRGB #35445D (muted blue navy, visibly blue rather than near-black), gold around #BEA782. A clear subtle compass emblem roughly 20 percent of the total image width and centered about 75 percent down the image. Keep embroidery light and sparse, not a heavy ornamental frame.
Lighting: color/albedo only with delicate cloth variation. No strong baked illumination or shadows, no metallic specular shine, no vignette, Unity supplies the 3D light.
Constraints: no people, body, garment outline, stand, mannequin, terrain, border margin, letters, words, labels, compass letters N/E/S/W, logos, watermark, collage, UV wireframe, big gold plates, bright magic effects or photoreal perspective. Opaque RGB albedo, no transparency. Return the texture itself.
```
