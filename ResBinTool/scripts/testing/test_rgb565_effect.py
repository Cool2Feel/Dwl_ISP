#!/usr/bin/env python3
import struct
import os

BIN_PATH = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\icon\OSD_source.bin'
PALETTE_PATH = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\ax32_platform_demo\resource\icon\palette.bin'
OUTPUT_DIR_RGB565 = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\decoded_icons_rgb565'
OUTPUT_DIR_BGRA = r'd:\jrx\2026\sdk\master\HM020F_SVN300\HM020F\firmware\decoded_icons_bgra'

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

TRANSPARENT_INDEX = 0xF9

def rgb565_to_rgb888(rgb565):
    r = ((rgb565 >> 11) & 0x1F) * 255 // 31
    g = ((rgb565 >> 5) & 0x3F) * 255 // 63
    b = (rgb565 & 0x1F) * 255 // 31
    return (r, g, b)

def read_palette_rgb565(palette_path):
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

def read_palette_bgra(palette_path):
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
            if a == 0 and i > 0:
                a = 255
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
            'width': width, 'height': height, 'data_offset': data_offset
        })
    return icons

def decode_icon(bin_data, icon_info, palette):
    width = icon_info['width']
    height = icon_info['height']
    data_offset = icon_info['data_offset']
    pixels = []
    for y in range(height - 1, -1, -1):
        row = []
        for x in range(width):
            idx = bin_data[data_offset + y * width + x]
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
    bmp_info_header = struct.pack('<IIIHHIIIIII', 40, width, height, 1, 32, 0, 0, 2835, 2835, 0, 0)
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

def main():
    os.makedirs(OUTPUT_DIR_RGB565, exist_ok=True)
    os.makedirs(OUTPUT_DIR_BGRA, exist_ok=True)
    
    with open(BIN_PATH, 'rb') as f:
        bin_data = f.read()
    
    palette_rgb565 = read_palette_rgb565(PALETTE_PATH)
    palette_bgra = read_palette_bgra(PALETTE_PATH)
    
    icons = read_bin_header(bin_data)
    
    print("=" * 70)
    print("RGB565 vs BGRA Decoding Comparison")
    print("=" * 70)
    print(f"{'Icon':<25} {'RGB565 Colors':<15} {'BGRA Colors':<15}")
    print("-" * 70)
    
    for icon in icons:
        osd_freq = {}
        for y in range(icon['height']):
            for x in range(icon['width']):
                idx = bin_data[icon['data_offset'] + y * icon['width'] + x]
                osd_freq[idx] = osd_freq.get(idx, 0) + 1
        
        rgb565_colors = set()
        bgra_colors = set()
        
        for idx in osd_freq.keys():
            rgb565_colors.add(palette_rgb565[idx][:3])
            bgra_colors.add(palette_bgra[idx][:3])
        
        print(f"{icon['name']:<25} {len(rgb565_colors):<15} {len(bgra_colors):<15}")
        
        pixels_rgb565 = decode_icon(bin_data, icon, palette_rgb565)
        pixels_bgra = decode_icon(bin_data, icon, palette_bgra)
        
        write_bmp(os.path.join(OUTPUT_DIR_RGB565, f"{icon['name']}.bmp"), pixels_rgb565)
        write_bmp(os.path.join(OUTPUT_DIR_BGRA, f"{icon['name']}.bmp"), pixels_bgra)
    
    print("\n" + "=" * 70)
    print(f"RGB565 decoded icons saved to: {OUTPUT_DIR_RGB565}")
    print(f"BGRA decoded icons saved to: {OUTPUT_DIR_BGRA}")
    print("=" * 70)

if __name__ == '__main__':
    main()
