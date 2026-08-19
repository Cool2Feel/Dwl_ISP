#!/usr/bin/env python3
import struct
import os

BIN_PATH = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\icon\OSD_source.bin'
PALETTE_PATH = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\icon\palette.bin'
OUTPUT_DIR = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\decoded_icons'
ORIGINAL_ICON_DIR = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\icon\iconSrc'

ICON_NAMES = [
    "iconGameSnakeWall",
    "iconMenuMusicPause",
    "iconMenuMusicPlay",
    "iconMTBattery0",
    "iconMTBattery1",
    "iconMTBattery2",
    "iconMTBattery3",
    "iconMTBattery4",
    "iconMTBattery5",
    "iconMTMicroscope",
    "iconMTNULL",
    "iconMTPause",
    "iconMTPhoto",
    "iconMTPhoto3",
    "iconMTPhotoFocusRed",
    "iconMTPhotoFocusYellow",
    "iconMTPlay",
    "iconMTRecord",
    "iconMTRecord1080P",
    "iconMTRecord720P",
    "iconMTRecording",
    "iconMTRecordVGA"
]

TRANSPARENT_INDEX = 0xF9

BG_COLOR_INDEX = 0

def rgb565_to_rgb888(rgb565):
    r = ((rgb565 >> 11) & 0x1F) * 255 // 31
    g = ((rgb565 >> 5) & 0x3F) * 255 // 63
    b = (rgb565 & 0x1F) * 255 // 31
    return (r, g, b)

def read_palette(palette_path):
    with open(palette_path, 'rb') as f:
        data = f.read()
    palette = []
    for i in range(0, len(data), 4):
        if i + 3 < len(data):
            color = struct.unpack('<I', data[i:i+4])[0]
            rgb565_val = color & 0xFFFF
            r, g, b = rgb565_to_rgb888(rgb565_val)
            tag_byte = (color >> 16) & 0xFF
            a = (color >> 24) & 0xFF
            
            if tag_byte == 0x1F:
                if a == 0:
                    a = 255
            elif tag_byte == 0x00:
                if i > 0:
                    a = 0
            elif tag_byte == 0x10:
                a = 128
            
            palette.append((r, g, b, a))
        else:
            palette.append((0, 0, 0, 0))
    
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
            'index': i,
            'name': ICON_NAMES[i],
            'width': width,
            'height': height,
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

def color_distance(c1, c2):
    r1, g1, b1, _ = c1
    r2, g2, b2, _ = c2
    return ((r1-r2)**2 + (g1-g2)**2 + (b1-b2)**2)**0.5

def build_index_mapping(osd_freq, osd_palette, bmp_freq, bmp_palette):
    osd_indices = sorted(osd_freq.keys(), key=lambda x: -osd_freq[x])
    bmp_indices = sorted(bmp_freq.keys(), key=lambda x: -bmp_freq[x])
    
    mapping = {}
    for i, osd_idx in enumerate(osd_indices):
        if i < len(bmp_indices):
            mapping[osd_idx] = bmp_indices[i]
    
    return mapping

def decode_icon(bin_data, icon_info, palette, index_mapping=None):
    width = icon_info['width']
    height = icon_info['height']
    data_offset = icon_info['data_offset']
    pixels = []
    for y in range(height - 1, -1, -1):
        row = []
        for x in range(width):
            idx = bin_data[data_offset + y * width + x]
            if index_mapping and idx in index_mapping:
                idx = index_mapping[idx]
            r, g, b, a = palette[idx]
            if idx == TRANSPARENT_INDEX:
                a = 0
            row.append((r, g, b, a))
        pixels.append(row)
    return pixels

def write_bmp(filename, pixels):
    height = len(pixels)
    width = len(pixels[0]) if height > 0 else 0
    
    bmp_file_header = struct.pack('<2sIHHI', b'BM', 0, 0, 0, 54)
    
    bmp_info_header = struct.pack('<IIIHHIIIIII',
        40,              
        width,           
        height,          
        1,               
        32,              
        0,               
        0,               
        2835,            
        2835,            
        0,               
        0               
    )
    
    raw_data = []
    for row in pixels:
        for pixel in row:
            r, g, b, a = pixel
            raw_data.append(b)
            raw_data.append(g)
            raw_data.append(r)
            raw_data.append(a)
    
    raw_data = bytes(raw_data)
    file_size = 54 + len(raw_data)
    bmp_file_header = struct.pack('<2sIHHI', b'BM', file_size, 0, 0, 54)
    
    with open(filename, 'wb') as f:
        f.write(bmp_file_header)
        f.write(bmp_info_header)
        f.write(raw_data)

def write_pnm(filename, pixels):
    height = len(pixels)
    width = len(pixels[0]) if height > 0 else 0
    
    with open(filename, 'w') as f:
        f.write(f'P3\n{width} {height}\n255\n')
        for row in pixels:
            for pixel in row:
                r, g, b, a = pixel
                f.write(f'{r} {g} {b} ')
            f.write('\n')

def main():
    print("=" * 60)
    print("OSD_source.bin Reverse Decoder")
    print("=" * 60)
    
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    
    print(f"\nReading palette from: {PALETTE_PATH}")
    osd_palette = read_palette(PALETTE_PATH)
    print(f"OSD palette loaded: {len(osd_palette)} colors")
    
    print(f"\nReading binary from: {BIN_PATH}")
    with open(BIN_PATH, 'rb') as f:
        bin_data = f.read()
    print(f"Binary size: {len(bin_data)} bytes")
    
    print("\nParsing header...")
    icons = read_bin_header(bin_data)
    print(f"Found {len(icons)} icons")
    
    print("\nDecoding icons:")
    print("-" * 60)
    
    for icon in icons:
        print(f"Icon {icon['index']:2d}: {icon['name']} - {icon['width']}x{icon['height']}")
        
        orig_bmp_path = os.path.join(ORIGINAL_ICON_DIR, f"{icon['name']}.bmp")
        index_mapping = None
        palette = osd_palette
        
        if os.path.exists(orig_bmp_path):
            bmp_palette = extract_palette_from_bmp(orig_bmp_path)
            osd_freq = get_index_frequency(bin_data, icon)
            bmp_freq = get_bmp_index_frequency(orig_bmp_path)
            print(f"        BMP index bmp_palette: {bmp_palette}")
            print(f"        OSD index frequency: {osd_freq}")
            
            index_mapping = build_index_mapping(osd_freq, osd_palette, bmp_freq, bmp_palette)
            palette = bmp_palette
            print(f"        Using index mapping: {index_mapping}")
        
        pixels = decode_icon(bin_data, icon, palette, index_mapping)
        
        bmp_filename = os.path.join(OUTPUT_DIR, f"{icon['name']}.bmp")
        write_bmp(bmp_filename, pixels)
        
        print(f"        -> {bmp_filename}")
    
    print("\n" + "-" * 60)
    print(f"Successfully decoded {len(icons)} icons to: {OUTPUT_DIR}")
    print("=" * 60)

if __name__ == '__main__':
    main()