#!/usr/bin/env python3
"""
分析 AX329X DestBin.bin 文件结构
"""
import struct
import sys

def analyze_destbin(filepath):
    with open(filepath, 'rb') as f:
        data = f.read()
    
    print(f"文件大小: {len(data)} bytes ({len(data)/1024:.2f} KB)")
    print()
    
    # 检查文件头
    if len(data) >= 12:
        signature = data[4:8].decode('ascii', errors='ignore')
        print(f"BLDR 签名: '{signature}' at offset 0x0004")
        
        # 版本信息
        version_raw = struct.unpack('<I', data[8:12])[0]
        major = (version_raw >> 16) & 0xFF
        minor = (version_raw >> 8) & 0xFF
        patch = version_raw & 0xFF
        print(f"版本号: v{major}.{minor}.{patch} (raw: 0x{version_raw:08X})")
        
        # 序列号
        serial = data[16:24].decode('ascii', errors='ignore')
        serial = ''.join(c for c in serial if 32 <= ord(c) <= 126)
        print(f"序列号: '{serial.strip()}'")
    
    print()
    print("=" * 80)
    print("检查候选偏移位置:")
    print("=" * 80)
    
    candidates = [
        (0x86A00, "AX329X 标准偏移"),
        (0x9DC00, "JT529X 标准偏移"),
        (0x80000, "512 KB"),
        (0xA0000, "640 KB"),
    ]
    
    for offset, desc in candidates:
        if offset + 64 > len(data):
            print(f"\n✗ 偏移 0x{offset:06X} ({desc}): 超出文件范围")
            continue
        
        print(f"\n{'─' * 80}")
        print(f"偏移 0x{offset:06X} ({desc}):")
        print(f"{'─' * 80}")
        
        # 读取前 6 个资源表条目（每个 8 字节）
        entries = []
        for i in range(6):
            entry_offset = offset + i * 8
            if entry_offset + 8 > len(data):
                break
            
            addr = struct.unpack('<I', data[entry_offset:entry_offset+4])[0]
            length = struct.unpack('<I', data[entry_offset+4:entry_offset+8])[0]
            entries.append((addr, length))
            
            print(f"  Entry[{i}]: Address=0x{addr:08X}, Length={length:8d} (0x{length:08X})")
        
        # 分析地址模式
        if len(entries) >= 3:
            addrs = [e[0] for e in entries[:3]]
            lengths = [e[1] for e in entries[:3]]
            
            print(f"\n  地址模式分析:")
            print(f"    是否递增: {addrs[0] < addrs[1] < addrs[2]}")
            print(f"    是否为相对偏移 (< 1MB): {all(a < 0x100000 for a in addrs)}")
            print(f"    是否有非零值: {any(a > 0 for a in addrs)}")
            
            # 判断是否为有效的资源表
            is_valid_table = False
            reasons = []
            
            # 方法 1: 绝对地址且递增
            if all(a > offset for a in addrs) and addrs[0] < addrs[1] < addrs[2]:
                is_valid_table = True
                reasons.append("绝对地址且严格递增")
            
            # 方法 2: 相对偏移且递增
            elif all(a > 0 and a < 0x100000 for a in addrs) and addrs[0] < addrs[1] < addrs[2]:
                is_valid_table = True
                reasons.append("相对偏移且严格递增")
            
            # 方法 3: 至少有两个递增的非零地址
            elif any(addrs[i] < addrs[i+1] for i in range(len(addrs)-1)) and any(a > 0 for a in addrs):
                # 额外检查：长度字段是否合理
                if any(l > 0 and l < 0x100000 for l in lengths):
                    is_valid_table = True
                    reasons.append("部分递增且有合理长度")
            
            if is_valid_table:
                print(f"    ✓ 可能是有效的资源表: {', '.join(reasons)}")
                
                # 尝试解析第一个资源
                first_addr = entries[0][0]
                first_len = entries[0][1]
                
                # 如果是相对偏移，需要加上基址
                if first_addr < 0x100000:
                    actual_addr = offset + first_addr
                else:
                    actual_addr = first_addr
                
                if actual_addr + min(first_len, 16) <= len(data):
                    header = data[actual_addr:actual_addr+min(first_len, 16)]
                    print(f"\n  第一个资源预览 (offset=0x{actual_addr:06X}, size={first_len}):")
                    print(f"    前16字节: {' '.join(f'{b:02X}' for b in header)}")
                    
                    # 检测类型
                    if header[0:3] == b'\xFF\xD8\xFF':
                        print(f"    类型: JPEG 图片")
                    elif header[0:2] == b'BM':
                        print(f"    类型: BMP 图片")
                    elif header[0:4] == b'RIFF' and header[8:12] == b'WAVE':
                        print(f"    类型: WAV 音频")
                    elif len(header) >= 2 and header[0:2] == b'MX':
                        print(f"    类型: Font 字体")
                    else:
                        print(f"    类型: 未知二进制")
            else:
                print(f"    ✗ 不是有效的资源表")
                
                # 显示原始数据的前 32 字节
                raw_data = data[offset:offset+32]
                print(f"    原始数据: {' '.join(f'{b:02X}' for b in raw_data)}")
    
    print("\n" + "=" * 80)
    print("建议:")
    print("=" * 80)
    print("如果上述所有候选位置都不是有效的资源表，可能需要:")
    print("1. 使用更宽松的扫描策略（搜索整个文件）")
    print("2. 检查是否有平台特定的偏移配置")
    print("3. 手动指定正确的偏移位置")

if __name__ == '__main__':
    if len(sys.argv) < 2:
        print("用法: python AnalyzeAX329X_DestBin.py <DestBin.bin>")
        sys.exit(1)
    
    filepath = sys.argv[1]
    analyze_destbin(filepath)
