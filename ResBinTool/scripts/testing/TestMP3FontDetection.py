import struct
import os

file_path = "ax32_platform_demo/resource/resTable/MP3font.bin"

if not os.path.exists(file_path):
    print(f"File not found: {file_path}")
    exit(1)

file_size = os.path.getsize(file_path)
print(f"=== MP3font.bin Detection Test ===")
print(f"File size: {file_size} bytes ({file_size / 1024:.2f} KB)")
print()

with open(file_path, 'rb') as f:
    data = f.read(100)
    
    # First 4 bytes: character count
    charCount = struct.unpack_from('<I', data, 0)[0]
    print(f"Character count: {charCount}")
    print()
    
    # Check detection logic
    print("Detection logic check:")
    print(f"  Char count in range [100, 50000]: {100 <= charCount <= 50000}")
    print(f"  Expected result: Font ✓")
    print()
    
    if 100 <= charCount <= 50000:
        print("✅ MP3font.bin will be correctly detected as Font type")
    else:
        print("❌ MP3font.bin will NOT be detected as Font type")
    
    print()
    print("First 5 character entries:")
    print(f"{'Index':<6} {'Offset':<12} {'Width':<8} {'Height':<8}")
    print("-" * 40)
    
    offset = 4
    for i in range(5):
        entry = struct.unpack_from('<IHH', data, offset)
        print(f"{i:<6} 0x{entry[0]:08X} {entry[1]:<8} {entry[2]:<8}")
        offset += 8
