"""Copies the generated story assets into Resources/Story: the FBX files, with import settings that map their materials
onto HDRP materials, and the generated textures, with materials that use them (colour + normal map).

    python install_story.py <folder make_story_assets.py wrote> <Unity project folder>
"""
import os
import re
import shutil
import sys
import uuid

src, project = sys.argv[1], sys.argv[2]
models = project + '/Assets/Hololuminescent/Resources/Models'
story = project + '/Assets/Hololuminescent/Resources/Story'
mats = story + '/Materials'
texs = story + '/Textures'
os.makedirs(mats, exist_ok=True)
os.makedirs(texs, exist_ok=True)


def guid_of(meta):
    return next(l.split(': ')[1].strip() for l in open(meta, encoding='utf-8') if l.startswith('guid:'))


def write_meta(path, template_meta):
    """`path`.meta from a template, keeping the guid if the asset already had one (so references stay valid)."""
    meta = open(template_meta, encoding='utf-8', newline='').read()
    keep = guid_of(path + '.meta') if os.path.exists(path + '.meta') else uuid.uuid4().hex
    open(path + '.meta', 'w', encoding='utf-8', newline='').write(meta.replace(guid_of(template_meta), keep))
    return keep


BASE = models + '/UTCN/UTCN_Dark.mat'


def material(name, rgb, base_map=None, normal_map=None, smoothness=None, metallic=None):
    """A copy of UTCN_Dark.mat (HDRP Lit): colour (sRGB, as the Inspector shows it), optionally with a colour map and a
    normal map (texture guids). Rewritten each run, so changes here reach Unity."""
    path = f'{mats}/{name}.mat'
    s = open(BASE, encoding='utf-8', newline='').read()
    nl = '\r\n' if '\r\n' in s else '\n'
    s = s.replace('m_Name: UTCN_Dark', 'm_Name: ' + name)

    def rep(a, b):
        nonlocal s
        a, b = a.replace('\n', nl), b.replace('\n', nl)
        assert s.count(a) == 1, a
        s = s.replace(a, b)
    for key in ('_BaseColor', '_Color'):   # any value: Unity re-saves them with float noise (0.13699996)
        s, n = re.subn(r'    - %s: \{r: [^}]*\}' % key, '    - %s: {r: %s, g: %s, b: %s, a: 1}' % ((key,) + rgb), s)
        assert n == 1, key
    if base_map:
        rep('    - _BaseColorMap:\n        m_Texture: {fileID: 0}', '    - _BaseColorMap:\n        m_Texture: {fileID: 2800000, guid: %s, type: 3}' % base_map)
        rep('    - _MainTex:\n        m_Texture: {fileID: 0}', '    - _MainTex:\n        m_Texture: {fileID: 2800000, guid: %s, type: 3}' % base_map)
    if normal_map:
        rep('    - _NormalMap:\n        m_Texture: {fileID: 0}', '    - _NormalMap:\n        m_Texture: {fileID: 2800000, guid: %s, type: 3}' % normal_map)
        rep('  m_ValidKeywords:\n', '  m_ValidKeywords:\n  - _NORMALMAP\n')   # HDRP only samples the normal map with this on
    if smoothness is not None:
        rep('    - _Smoothness: 0.65', '    - _Smoothness: %s' % smoothness)
    if metallic is not None:
        rep('    - _Metallic: 0.3', '    - _Metallic: %s' % metallic)
    open(path, 'w', encoding='utf-8', newline='').write(s)
    return write_meta(path, BASE + '.meta')


folder_meta = models + '.meta'
for folder in (mats, texs):
    if not os.path.exists(folder + '.meta'):
        write_meta(folder, folder_meta)

# Textures: colour maps like Engine's base colour, normal maps like Engine's normal map.
COLOUR_META = models + '/Engine/textures/Reactor_Base_BaseColor.png.meta'
NORMAL_META = models + '/Engine/textures/Reactor_Base_Normal.png.meta'
tex = {}
for f in sorted(os.listdir(src)):
    if f.endswith('.png'):
        shutil.copyfile(f'{src}/{f}', f'{texs}/{f}')
        tex[f[:-4]] = write_meta(f'{texs}/{f}', NORMAL_META if f.endswith('_N.png') else COLOUR_META)
        print('installed', f)


def unlit(name, rgb):
    """An HDRP Unlit material (a copy of VQ1Hologram's Faint Cyan.mat) in one flat colour: no light changes it."""
    path = f'{mats}/{name}.mat'
    template = project + '/Assets/VQ1Hologram/Generated/Faint Cyan.mat'
    s = open(template, encoding='utf-8', newline='').read()
    s = s.replace('m_Name: Faint Cyan', 'm_Name: ' + name)
    for key in ('_UnlitColor', '_Color'):
        s, n = re.subn(r'    - %s: \{r: [^}]*\}' % key, '    - %s: {r: %s, g: %s, b: %s, a: 1}' % ((key,) + rgb), s)
        assert n == 1, key
    open(path, 'w', encoding='utf-8', newline='').write(s)
    return write_meta(path, template + '.meta')


def textured(name, texture, smoothness, metallic):
    """White-tinted, so the texture gives the colour."""
    return material(name, (1, 1, 1), tex.get(texture), tex.get(texture + '_N'), smoothness, metallic)


dark = guid_of(models + '/UTCN/UTCN_Dark.mat.meta')
mapping = {
    'Graphite': textured('StoryPlastic', 'Tex_Plastic', 0.45, 0.1),
    'UTCNRed': textured('StoryPaint', 'Tex_Paint', 0.55, 0.1),
    'Concrete': textured('StoryConcrete', 'Tex_Concrete', 0.2, 0.0),
    'Leaf': textured('StoryGrass', 'Tex_Grass', 0.15, 0.0),
    'Fins': textured('StoryMetal', 'Tex_Metal', 0.6, 0.8),      # brushed aluminium; light, not white (invisible on the HLD)
    'Solar': textured('StorySolar', 'Tex_Solar', 0.85, 0.3),
    'PCB': textured('StoryPCB', 'Tex_PCB', 0.5, 0.2),
    'Glass': material('AIRIGlass', (0.16, 0.22, 0.27), smoothness=0.92, metallic=0.4),
    # Headlines: black faces, unlit, since the spots light the stage past white and even a near-black lit face came out
    # grey; dark grey lit sides, so the letters stand out hard against the white and their depth still reads.
    'TextFace': unlit('StoryInk', (0.02, 0.02, 0.022)),
    'TextSide': material('StoryInkSide', (0.13, 0.13, 0.14), smoothness=0.15, metallic=0.0),
}

template = open(models + '/UTCN/UTCN.fbx.meta', encoding='utf-8', newline='').read()
nl = '\r\n' if '\r\n' in template else '\n'
start, end = template.index('  externalObjects:'), template.index('  materials:')
external = '  externalObjects:' + nl + ''.join(
    f'  - first:{nl}      type: UnityEngine:Material{nl}      assembly: UnityEngine.CoreModule{nl}'
    f'      name: {name}{nl}    second: {{fileID: 2100000, guid: {g}, type: 2}}{nl}'
    for name, g in mapping.items())
template = template[:start] + external + template[end:]
old = guid_of(models + '/UTCN/UTCN.fbx.meta')

for f in sorted(os.listdir(src)):
    if not f.endswith('.fbx'):
        continue
    shutil.copyfile(f'{src}/{f}', f'{story}/{f}')
    meta = f'{story}/{f}.meta'
    keep = guid_of(meta) if os.path.exists(meta) else uuid.uuid4().hex   # keep existing guids stable
    open(meta, 'w', encoding='utf-8', newline='').write(template.replace(old, keep))
    print('installed', f)
