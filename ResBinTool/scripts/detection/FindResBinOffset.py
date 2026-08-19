import struct
import os

def check_offset(file_path, offset, label=""):
    """检查指定偏移处的数据"""
    if not os.path.exists(file_path):
        return
    
    file_size = os.path.getsize(file_path)
    if offset >= file_size:
        print(f"  {label} Offset 0x{offset:X}: OUT OF BOUNDS (file size: 0x{file_size:X})")
        return
    
    with open(file_path, 'rb') as f:
        f.seek(offset)
        data = f.read(32)
        
        if len(data) < 8:
            print(f"  {label} Offset 0x{offset:X}: Insufficient data")
            return
        
        addr1, addr2 = struct.unpack_from('<II', data, 0)
        
        # 判断是否为有效资源表
        is_valid = False
        reason = ""
        
        if addr1 > offset and addr2 > addr1 and addr2 < file_size:
            is_valid = True
            reason = "Valid absolute addresses"
        elif addr1 < 0x100000 and addr2 < 0x100000 and addr2 > addr1:
            is_valid = True
            reason = "Valid relative offsets"
        
        status = "VALID" if is_valid else "INVALID"
        print(f"  {label} Offset 0x{offset:X}: addr1=0x{addr1:08X}, addr2=0x{addr2:08X} - {status}")
        if reason:
            print(f"         Reason: {reason}")
        
        return is_valid, addr1, addr2

print("="*70)
print("JT529X DestBin.bin - Finding RES.BIN Location")
print("="*70)

jt_path = "ax32_platform_demo/output/DestBin.bin"
if os.path.exists(jt_path):
    file_size = os.path.getsize(jt_path)
    print(f"File size: {file_size:,} bytes (0x{file_size:X})")
    print()
    
    # 检查常见偏移
    print("Checking common offsets:")
    for offset in [0x9DC00, 0x100000, 0x200000, 0x300000, 0x400000, 0x450000, 0x480000]:
        check_offset(jt_path, offset)
    
    # 暴力扫描（步长 4KB）
    print("\nScanning for valid resource table (step 4KB)...")
    found = False
    for offset in range(0x80000, min(file_size - 1024, 0x500000), 0x1000):
        result = check_offset(jt_path, offset, "[SCAN]")
        if result and result[0]:  # is_valid
            print(f"\n  >>> FOUND at 0x{offset:X} <<<")
            found = True
            
            # 显示更多条目
            with open(jt_path, 'rb') as f:
                f.seek(offset)
                print(f"\n  Resource Table Entries:")
                for i in range(10):
                    entry = f.read(8)
                    if len(entry) < 8:
                        break
                    a, l = struct.unpack_from('<II', entry, 0)
                    if a == 0 and l == 0:
                        print(f"    [{i}] END OF TABLE")
                        break
                    print(f"    [{i}] Addr=0x{a:08X}, Len={l}")
            break
    
    if not found:
        print("\n  ✗ No valid resource table found")

print("\n" + "="*70)
print("AX329X DestBin.bin - Analysis")
print("="*70)

ax_path = "D:/dwl/work/2026/JT/JX_SDK/AX329X/firmware/ax32_platform_demo/output/DestBin.bin"
if os.path.exists(ax_path):
    file_size = os.path.getsize(ax_path)
    print(f"File size: {file_size:,} bytes (0x{file_size:X})")
    print()
    
    # 检查标准偏移
    print("Checking standard offset 0x9DC00:")
    check_offset(ax_path, 0x9DC00)
    
    # 检查文件末尾附近
    print("\nChecking near end of file:")
    for offset in [file_size - 0x10000, file_size - 0x8000, file_size - 0x4000]:
        if offset > 0:
            check_offset(ax_path, offset)
    
    # 分析头部字段可能指示的偏移
    print("\nAnalyzing header fields for RES.BIN offset hints:")
    with open(ax_path, 'rb') as f:
        header = f.read(512)
        
        # 检查偏移 0x18-0x1F 等位置
        for hint_offset in [0x18, 0x1C, 0x20, 0x24, 0x28, 0x2C]:
            if hint_offset + 4 <= len(header):
                value = struct.unpack_from('<I', header, hint_offset)[0]
                if value > 0x1000 and value < file_size:
                    print(f"  Offset 0x{hint_offset:X}: 0x{value:08X} ({value:,}) - POSSIBLE HINT")
                    check_offset(ax_path, value, "  [HINT]")
