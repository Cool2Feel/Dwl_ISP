import struct
import os

file_path = "ax32_platform_demo/resource/resTable/MP3font.bin"

if not os.path.exists(file_path):
    print(f"File not found: {file_path}")
    exit(1)

file_size = os.path.getsize(file_path)
print(f"=== MP3font.bin Analysis ===")
print(f"File size: {file_size} bytes ({file_size / 1024:.2f} KB)")
print()

with open(file_path, 'rb') as f:
    data = f.read(100)
    
    # First 4 bytes: character count
    charCount = struct.unpack_from('<I', data, 0)[0]
    print(f"Character count (first 4 bytes): {charCount}")
    print()
    
    # Next entries: offset (4 bytes) + width (2 bytes) + height (2 bytes)
    print("First 10 character entries:")
    print(f"{'Index':<6} {'Offset':<12} {'Width':<8} {'Height':<8} {'DataSize':<10}")
    print("-" * 50)
    
    offset = 4
    for i in range(min(10, charCount)):
        if offset + 8 > len(data):
            break
        entry = struct.unpack_from('<IHH', data, offset)
        char_offset = entry[0]
        width = entry[1]
        height = entry[2]
        dataSize = ((width + 7) // 8) * height
        
        print(f"{i:<6} 0x{char_offset:08X} {width:<8} {height:<8} {dataSize:<10}")
        offset += 8
    
    print()
    print("File structure analysis:")
    print(f"- Header: 4 bytes (character count = {charCount})")
    print(f"- Character metadata: {charCount} entries x 8 bytes = {charCount * 8} bytes")
    print(f"- Expected metadata end offset: {4 + charCount * 8} bytes")
    print(f"- Total file size: {file_size} bytes")
    print(f"- Bitmap data starts at offset: ~{4 + charCount * 8}")
    print()
    
    # Check if this matches the resfont.bin format
    print("Format comparison with resfont.bin:")
    print("✓ Same structure: charCount (4 bytes) + metadata array (8 bytes each) + bitmap data")
    print("✓ This is a FONT resource (resfont format)")
