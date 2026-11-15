"""Copies the generated story FBX files into Resources/Story with import settings that map their materials
onto the project's HDRP materials, and makes the two new ones (Concrete, Leaf)."""
import os
import shutil
import sys
import uuid

src, project = sys.argv[1], sys.argv[2]
models = project + '/Assets/Hololuminescent/Resources/Models'
story = project + '/Assets/Hololuminescent/Resources/Story'
mats = story + '/Materials'
os.makedirs(mats, exist_ok=True)


def guid_of(meta):
    return next(l.split(': ')[1].strip() for l in open(meta, encoding='utf-8') if l.startswith('guid:'))


def new_material(name, rgb):
    """A copy of UTCN_Dark.mat (HDRP Lit) in another colour (sRGB, as the Inspector shows it)."""
    path = f'{mats}/{name}.mat'
    if os.path.exists(path):
        return guid_of(path + '.meta')
    s = open(models + '/UTCN/UTCN_Dark.mat', encoding='utf-8', newline='').read()
    s = s.replace('m_Name: UTCN_Dark', 'm_Name: ' + name)
    for key in ('_BaseColor', '_Color'):
        a = '    - %s: {r: 0.137, g: 0.122, b: 0.125, a: 1}' % key
        assert s.count(a) == 1, a
        s = s.replace(a, '    - %s: {r: %s, g: %s, b: %s, a: 1}' % ((key,) + rgb))
    open(path, 'w', encoding='utf-8', newline='').write(s)
    meta = open(models + '/UTCN/UTCN_Dark.mat.meta', encoding='utf-8', newline='').read()
    g = uuid.uuid4().hex
    open(path + '.meta', 'w', encoding='utf-8', newline='').write(meta.replace(guid_of(models + '/UTCN/UTCN_Dark.mat.meta'), g))
    return g


folder_meta = open(models + '.meta', encoding='utf-8', newline='').read()
if not os.path.exists(mats + '.meta'):
    open(mats + '.meta', 'w', encoding='utf-8', newline='').write(folder_meta.replace(guid_of(models + '.meta'), uuid.uuid4().hex))

dark = guid_of(models + '/UTCN/UTCN_Dark.mat.meta')
red = guid_of(models + '/UTCN/UTCN_Red.mat.meta')
black = guid_of(models + '/AIRI/AIRI_Body.mat.meta')
mapping = {
    'Graphite': dark, 'UTCNRed': red,
    'TextFace': dark,                           # headlines: faces in the UTCN logo's grey...
    'TextSide': new_material('Concrete', (0.46, 0.46, 0.48)),   # ...lighter sides, so the depth of the letters reads
    'Concrete': new_material('Concrete', (0.46, 0.46, 0.48)),
    'Glass': new_material('AIRIGlass', (0.10, 0.30, 0.33)),   # the AIRI building's teal glass
    'Leaf': new_material('Leaf', (0.12, 0.29, 0.15)),
    'Fins': new_material('AIRIFins', (0.70, 0.71, 0.72)),   # light, not white: white is invisible on the HLD
    'Solar': new_material('Solar', (0.09, 0.12, 0.24)),
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
