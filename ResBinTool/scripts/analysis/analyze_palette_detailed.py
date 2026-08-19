import os

palette_path = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\icon\palette.bin'

with open(palette_path, 'rb') as f:
    data = f.read()

print('=== Standard Custom Colors (ARGB format) ===')
standard_colors = {
    'ERROR': 0x00000000,
    'RED': 0xff0000ff,
    'GREEN': 0xff00ff00,
    'BLUE': 0xffff0000,
    'WHITE': 0xffffffff,
    'BLACK': 0xff000000,
    'TRANSFER': 0x00000001,
    'TBLACK': 0x80000000,
    'YELLOW': 0xff14aceb,
    'GARY1': 0xff757575,
    'BLUE1': 0xffe08000,
    'DBLUE': 0xff592902,
    'BLUE2': 0xffc0c060,
    'GARY2': 0xff303030,
    'GARY3': 0xff505050,
}

print(f"{'Name':<10} {'ARGB':<12} {'A':<4} {'R':<4} {'G':<4} {'B':<4}")
print('-' * 50)
for name, argb in standard_colors.items():
    a = (argb >> 24) & 0xFF
    r = (argb >> 16) & 0xFF
    g = (argb >> 8) & 0xFF
    b = argb & 0xFF
    print(f"{name:<10} 0x{argb:08X}  {a:3d}  {r:3d}  {g:3d}  {b:3d}")

print()
print('=== Actual File Data (BGRA format) ===')
print(f"{'Index':<8} {'Name':<10} {'BGRA':<14} {'R':<4} {'G':<4} {'B':<4} {'A':<4}")
print('-' * 70)

custom_indices = {
    0xFF: 'ERROR',
    0xFE: 'RED',
    0xFD: 'GREEN',
    0xFC: 'BLUE',
    0xFB: 'WHITE',
    0xFA: 'BLACK',
    0xF9: 'TRANSFER',
    0xF8: 'TBLACK',
    0xF7: 'YELLOW',
    0xF6: 'GARY1',
    0xF5: 'BLUE1',
    0xF4: 'DBLUE',
    0xF3: 'BLUE2',
    0xF2: 'GARY2',
    0xF1: 'GARY3',
}

for i in range(0xF0, 0x100):
    offset = i * 4
    b, g, r, a = data[offset:offset+4]
    name = custom_indices.get(i, '')
    bgra_str = f'{b:02X} {g:02X} {r:02X} {a:02X}'
    print(f"0x{i:02X}    {name:<10} {bgra_str:<14} {r:3d}  {g:3d}  {b:3d}  {a:3d}")

print()
print('=== Analysis: Compare Standard vs Actual ===')
print('Key observations:')
print('1. Actual file uses BGRA byte order')
print('2. Alpha values are all 0x00 (unused in embedded)')
print('3. Color values appear to be scaled/compressed')
print()
print('=== Check for scaling factor ===')
print('Standard WHITE: R=255, G=255, B=255')
print('Actual WHITE (0xFB): R=31, G=255, B=255')
print('-> R is 31 instead of 255, G and B match')
print()
print('Standard RED: R=255, G=0, B=0')
print('Actual RED (0xFE): R=31, G=248, B=0')
print('-> This does NOT match!')
print()
print('The actual colors in the file do NOT match standard definitions.')
print('They appear to be custom colors specific to this project.')
