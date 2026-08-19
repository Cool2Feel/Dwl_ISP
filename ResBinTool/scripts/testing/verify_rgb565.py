#!/usr/bin/env python3
import struct

palette_path = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\icon\palette.bin'

with open(palette_path, 'rb') as f:
    data = f.read()

def rgb565_to_rgb888(rgb565):
    r = ((rgb565 >> 11) & 0x1F) * 255 // 31
    g = ((rgb565 >> 5) & 0x3F) * 255 // 63
    b = (rgb565 & 0x1F) * 255 // 31
    return (r, g, b)

custom_indices = {
    0xFF: 'ERROR', 0xFE: 'RED', 0xFD: 'GREEN', 0xFC: 'BLUE',
    0xFB: 'WHITE', 0xFA: 'BLACK', 0xF9: 'TRANSFER', 0xF8: 'TBLACK',
    0xF7: 'YELLOW', 0xF6: 'GARY1', 0xF5: 'BLUE1', 0xF4: 'DBLUE',
    0xF3: 'BLUE2', 0xF2: 'GARY2', 0xF1: 'GARY3', 0xF0: ''
}

print("=" * 90)
print("PALETTE.BIN RGB565 DECODING VERIFICATION")
print("=" * 90)
print(f"{'Index':<8} {'Name':<10} {'Raw Bytes':<20} {'RGB565':<10} {'RGB565(RGB)':<20} {'RGB888':<20} {'Valid':<6}")
print("-" * 90)

for i in range(0xF0, 0x100):
    offset = i * 4
    b, g, r, a = data[offset:offset+4]
    
    raw_val = struct.unpack('<I', data[offset:offset+4])[0]
    rgb565_val = raw_val & 0xFFFF
    
    r5 = (rgb565_val >> 11) & 0x1F
    g6 = (rgb565_val >> 5) & 0x3F
    b5 = rgb565_val & 0x1F
    
    r8, g8, b8 = rgb565_to_rgb888(rgb565_val)
    
    name = custom_indices.get(i, '')
    is_valid = 'YES' if r == 0x1F else 'NO'
    
    print(f"0x{i:02X}    {name:<10} {b:02X} {g:02X} {r:02X} {a:02X}    0x{rgb565_val:04X}    ({r5:2d},{g6:2d},{b5:2d})           ({r8:3d},{g8:3d},{b8:3d})      {is_valid}")

print("\n" + "=" * 90)
print("COMPARISON WITH STANDARD COLORS")
print("=" * 90)

standard_colors = {
    'RED': (255, 0, 0),
    'GREEN': (0, 255, 0),
    'BLUE': (0, 0, 255),
    'WHITE': (255, 255, 255),
    'BLACK': (0, 0, 0)
}

print(f"{'Color':<10} {'Standard RGB888':<20} {'Decoded RGB888':<20} {'Match':<8}")
print("-" * 60)

for name, (sr, sg, sb) in standard_colors.items():
    idx = {v: k for k, v in custom_indices.items()}[name]
    offset = idx * 4
    raw_val = struct.unpack('<I', data[offset:offset+4])[0]
    rgb565_val = raw_val & 0xFFFF
    dr, dg, db = rgb565_to_rgb888(rgb565_val)
    
    match = "✅" if abs(sr-dr) < 10 and abs(sg-dg) < 10 and abs(sb-db) < 10 else "❌"
    print(f"{name:<10} ({sr:3d},{sg:3d},{sb:3d})           ({dr:3d},{dg:3d},{db:3d})           {match}")

print("\n" + "=" * 90)
print("LOW INDICES ANALYSIS (0x00-0x0F)")
print("=" * 90)
print(f"{'Index':<8} {'Raw Bytes':<20} {'RGB565':<10} {'RGB888':<20} {'Valid':<6}")
print("-" * 60)

for i in range(0, 0x10):
    offset = i * 4
    b, g, r, a = data[offset:offset+4]
    
    raw_val = struct.unpack('<I', data[offset:offset+4])[0]
    rgb565_val = raw_val & 0xFFFF
    
    r8, g8, b8 = rgb565_to_rgb888(rgb565_val)
    
    is_valid = 'YES' if r == 0x1F else 'NO'
    
    print(f"0x{i:02X}    {b:02X} {g:02X} {r:02X} {a:02X}    0x{rgb565_val:04X}    ({r8:3d},{g8:3d},{b8:3d})      {is_valid}")
