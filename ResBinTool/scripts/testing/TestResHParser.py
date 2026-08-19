import sys
import os

# Add the path to the ResBinManager executable
resbin_manager_path = r"d:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\tools\ResBinManager\bin\Debug\net6.0-windows\ResBinManager.exe"

print("="*70)
print("RES.H Parser Integration Test")
print("="*70)
print()

# Check if the executable exists
if not os.path.exists(resbin_manager_path):
    print(f"❌ ResBinManager.exe not found at: {resbin_manager_path}")
    print()
    print("Please build the project first:")
    print("  cd d:\\dwl\\work\\2026\\JT\\JX_SDK\\JT529X\\firmware\\tools\\ResBinManager")
    print("  dotnet build")
    sys.exit(1)

print(f"✅ ResBinManager.exe found")
print()

# Test files
test_files = [
    {
        "name": "JT529X",
        "destbin": r"D:\jrx\2026\code\JRX_SDK\JRX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin.bin",
        "resh": r"D:\jrx\2026\code\JRX_SDK\JRX_SDK\JT529X\firmware\ax32_platform_demo\resource\RES.H"
    },
    {
        "name": "AX329X",
        "destbin": r"D:\jrx\2026\code\JRX_SDK\JRX_SDK\AX329X\firmware\ax32_platform_demo\output\DestBin.bin",
        "resh": r"D:\jrx\2026\code\JRX_SDK\JRX_SDK\AX329X\firmware\ax32_platform_demo\resource\RES.H"
    }
]

print("Test Files:")
print("-" * 70)
for test in test_files:
    destbin_exists = os.path.exists(test["destbin"])
    resh_exists = os.path.exists(test["resh"])
    
    status = "✅" if (destbin_exists and resh_exists) else "❌"
    print(f"{status} {test['name']}:")
    print(f"   DestBin.bin: {'Found' if destbin_exists else 'NOT FOUND'}")
    print(f"   RES.H:       {'Found' if resh_exists else 'NOT FOUND'}")
    print()

print("="*70)
print("Instructions:")
print("="*70)
print()
print("1. Run ResBinManager manually:")
print(f"   {resbin_manager_path}")
print()
print("2. Load each DestBin.bin file")
print()
print("3. Check the Debug Output window for RES.H parsing logs:")
print("   - Look for '[ResHParser]' messages")
print("   - Verify platform detection")
print("   - Verify resource indices")
print()
print("4. Test font resource selection:")
print("   - Select RES_RESFONT resource")
print("   - Verify preview works correctly")
print("   - No crashes or errors")
print()
print("="*70)
print("Expected Debug Output:")
print("="*70)
print()
print("For JT529X:")
print("  [ResHParser] Platform: JT529X, Total Resources: 94")
print("  [ResHParser] RES_RESFONT = 79")
print("  [ResHParser] RES_RESFONTIDX = 80")
print()
print("For AX329X:")
print("  [ResHParser] Platform: AX329X, Total Resources: 13")
print("  [ResHParser] RES_RESFONT = 9")
print("  [ResHParser] RES_RESFONTIDX = 10")
print()
print("="*70)
