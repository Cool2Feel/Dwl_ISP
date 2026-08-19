#!/usr/bin/env python3
import struct
import os

BIN_PATH = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\icon\OSD_source.bin'
PALETTE_PATH = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\icon\palette.bin'
ORIGINAL_ICON_DIR = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\icon\iconSrc'

ICON_NAMES = [
    "iconGameSnakeWall", "iconMenuMusicPause", "iconMenuMusicPlay",
    "iconMTBattery0", "iconMTBattery1", "iconMTBattery2",
    "iconMTBattery3", "iconMTBattery4", "iconMTBattery5",
    "iconMTMicroscope", "iconMTNULL", "iconMTPause",
    "iconMTPhoto", "iconMTPhoto3", "iconMTPhotoFocusRed",
    "iconMTPhotoFocusYellow", "iconMTPlay", "iconMTRecord",
    "iconMTRecord1080P", "iconMTRecord720P", "iconMTRecording",
    "iconMTRecordVGA"
]

def read_palette(palette_path):
    with open(palette_path, 'rb') as f:
        data = f.read()
    palette = []
    for i in range(0, len(data), 4):
        if i + 3 < len(data):
            color = struct.unpack('<I', data[i:i+4])[0]
            b = color & 0xFF
            g = (color >> 8) & 0xFF
            r = (color >> 16) & 0xFF
            a = (color >> 24) & 0xFF
            palette.append((r, g, b, a))
    return palette

def extract_palette_from_bmp(bmp_path):
    with open(bmp_path, 'rb') as f:
        data = f.read()
    palette = []
    for i in range(256):
        pal_offset = 54 + i * 4
        if pal_offset + 3 < len(data):
            b = data[pal_offset]
            g = data[pal_offset + 1]
            r = data[pal_offset + 2]
            a = data[pal_offset + 3]
            palette.append((r, g, b, a))
        else:
            palette.append((0, 0, 0, 0))
    return palette

def read_bin_header(bin_data):
    icons = []
    for i in range(22):
        offset = i * 12
        width = struct.unpack('<I', bin_data[offset:offset+4])[0]
        height = struct.unpack('<I', bin_data[offset+4:offset+8])[0]
        data_offset = struct.unpack('<I', bin_data[offset+8:offset+12])[0]
        icons.append({
            'index': i, 'name': ICON_NAMES[i],
            'width': width, 'height': height,
            'data_offset': data_offset
        })
    return icons

def get_index_frequency(bin_data, icon_info):
    width = icon_info['width']
    height = icon_info['height']
    data_offset = icon_info['data_offset']
    freq = {}
    for y in range(height):
        for x in range(width):
            idx = bin_data[data_offset + y * width + x]
            freq[idx] = freq.get(idx, 0) + 1
    return freq

def get_bmp_index_frequency(bmp_path):
    with open(bmp_path, 'rb') as f:
        data = f.read()
    width = struct.unpack('<I', data[18:22])[0]
    height = struct.unpack('<I', data[22:26])[0]
    data_offset = struct.unpack('<I', data[10:14])[0]
    freq = {}
    for row in range(height):
        row_offset = data_offset + row * width
        for x in range(width):
            idx = data[row_offset + x]
            freq[idx] = freq.get(idx, 0) + 1
    return freq

def build_index_mapping(osd_freq, osd_palette, bmp_freq, bmp_palette):
    osd_indices = sorted(osd_freq.keys(), key=lambda x: -osd_freq[x])
    bmp_indices = sorted(bmp_freq.keys(), key=lambda x: -bmp_freq[x])
    mapping = {}
    for i, osd_idx in enumerate(osd_indices):
        if i < len(bmp_indices):
            mapping[osd_idx] = bmp_indices[i]
    return mapping

def main():
    print("=" * 80)
    print("INDEX MAPPING DETAILED ANALYSIS")
    print("=" * 80)
    
    with open(PALETTE_PATH, 'rb') as f:
        osd_palette = read_palette(PALETTE_PATH)
    
    with open(BIN_PATH, 'rb') as f:
        bin_data = f.read()
    
    icons = read_bin_header(bin_data)
    
    print("\n--- OSD Palette (High Indices 0xF0-0xFF) ---")
    print(f"{'Index':<8} {'RGBA':<20} {'Color Name'}")
    print("-" * 45)
    custom_indices = {
        0xFF: 'ERROR', 0xFE: 'RED', 0xFD: 'GREEN', 0xFC: 'BLUE',
        0xFB: 'WHITE', 0xFA: 'BLACK', 0xF9: 'TRANSFER', 0xF8: 'TBLACK',
        0xF7: 'YELLOW', 0xF6: 'GARY1', 0xF5: 'BLUE1', 0xF4: 'DBLUE',
        0xF3: 'BLUE2', 0xF2: 'GARY2', 0xF1: 'GARY3', 0xF0: ''
    }
    for i in range(0xF0, 0x100):
        r, g, b, a = osd_palette[i]
        name = custom_indices.get(i, '')
        print(f"0x{i:02X}    ({r:3d}, {g:3d}, {b:3d}, {a:3d})    {name}")
    
    print("\n" + "=" * 80)
    print("PER-ICON INDEX MAPPING ANALYSIS")
    print("=" * 80)
    
    for icon in icons:
        orig_bmp_path = os.path.join(ORIGINAL_ICON_DIR, f"{icon['name']}.bmp")
        
        if not os.path.exists(orig_bmp_path):
            print(f"\nIcon {icon['index']:2d}: {icon['name']}")
            print("      -> Original BMP not found, skipping...")
            continue
        
        osd_freq = get_index_frequency(bin_data, icon)
        bmp_freq = get_bmp_index_frequency(orig_bmp_path)
        bmp_palette = extract_palette_from_bmp(orig_bmp_path)
        
        osd_sorted = sorted(osd_freq.items(), key=lambda x: -x[1])
        bmp_sorted = sorted(bmp_freq.items(), key=lambda x: -x[1])
        
        print(f"\n{'='*60}")
        print(f"Icon {icon['index']:2d}: {icon['name']} ({icon['width']}x{icon['height']})")
        print(f"{'='*60}")
        
        print("\n--- OSD Index Frequency (from OSD_source.bin) ---")
        print(f"{'Index':<8} {'Count':<8} {'OSD Color (RGBA)'}")
        print("-" * 45)
        for idx, count in osd_sorted:
            r, g, b, a = osd_palette[idx]
            name = custom_indices.get(idx, '')
            print(f"0x{idx:02X}    {count:6d}    ({r:3d},{g:3d},{b:3d},{a:3d}) {name}")
        
        print("\n--- BMP Index Frequency (from original BMP) ---")
        print(f"{'Index':<8} {'Count':<8} {'BMP Color (RGBA)'}")
        print("-" * 45)
        for idx, count in bmp_sorted:
            r, g, b, a = bmp_palette[idx]
            print(f"0x{idx:02X}    {count:6d}    ({r:3d},{g:3d},{b:3d},{a:3d})")
        
        mapping = build_index_mapping(osd_freq, osd_palette, bmp_freq, bmp_palette)
        
        print("\n--- Index Mapping (OSD -> BMP) ---")
        print(f"{'OSD Index':<12} {'->':<4} {'BMP Index':<12} {'Mapping Reason'}")
        print("-" * 55)
        for osd_idx, bmp_idx in mapping.items():
            osd_r, osd_g, osd_b, osd_a = osd_palette[osd_idx]
            bmp_r, bmp_g, bmp_b, bmp_a = bmp_palette[bmp_idx]
            osd_name = custom_indices.get(osd_idx, '')
            print(f"0x{osd_idx:02X} ({osd_name:<10} -> 0x{bmp_idx:02X}")
            print(f"         OSD: ({osd_r:3d},{osd_g:3d},{osd_b:3d})")
            print(f"         BMP: ({bmp_r:3d},{bmp_g:3d},{bmp_b:3d})")

if __name__ == '__main__':
    main()
