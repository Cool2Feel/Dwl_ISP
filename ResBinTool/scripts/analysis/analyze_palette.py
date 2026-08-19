import os

palette_path = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\icon\palette.bin'

with open(palette_path, 'rb') as f:
    data = f.read()

print(f'File size: {len(data)} bytes')
print(f'Expected size: 1024 bytes')
print()

print('=== Color index 0 (Background) ===')
offset = 0 * 4
b, g, r, a = data[offset:offset+4]
print(f'BGRA: 0x{b:02X} 0x{g:02X} 0x{r:02X} 0x{a:02X}')
print(f'RGBA: ({r}, {g}, {b}, {a})')
print()

print('=== Custom colors (0xF0-0xFF) ===')
custom_colors = {
    0xFF: 'ERROR',
    0xFE: 'RED',
    0xFD: 'GREEN',
    0xFC: 'BLUE',
    0xFB: 'WHITE',
    0xFA: 'BLACK',
    0xF9: 'TRANSFER (transparent)',
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
    name = custom_colors.get(i, '')
    print(f'0x{i:02X} {name:25s}: BGRA=0x{b:02X} 0x{g:02X} 0x{r:02X} 0x{a:02X} -> RGBA=({r}, {g}, {b}, {a})')

print()
print('=== Analysis ===')
print(f'Index 0 (BGRA 10 84 00 00) -> RGBA(0, 132, 16, 0) - This is a GREENISH color')
print(f'Index 0xF9 (BGRA 00 00 00 00) -> RGBA(0, 0, 0, 0) - Fully transparent')
print(f'Index 0xFF (BGRA 00 00 00 00) -> RGBA(0, 0, 0, 0) - ERROR color (transparent?)')
