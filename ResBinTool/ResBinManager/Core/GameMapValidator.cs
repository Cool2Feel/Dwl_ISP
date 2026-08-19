using System;

namespace ResBinManager.Core
{
    /// <summary>
    /// 游戏地图资源验证结果
    /// </summary>
    public class GameMapValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Info { get; set; }
    }

    /// <summary>
    /// AX329x 游戏地图资源验证器
    /// game_block_map.bin, game_maze_map.bin, game_sokoban_map.bin 等
    /// </summary>
    public static class GameMapValidator
    {
        /// <summary>
        /// 验证游戏地图资源
        /// </summary>
        /// <param name="mapData">地图数据</param>
        /// <returns>验证结果</returns>
        public static GameMapValidationResult Validate(byte[] mapData)
        {
            var result = new GameMapValidationResult();

            // 1. 检查文件大小合理性（游戏地图通常较小）
            if (mapData.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "Game map data is empty";
                return result;
            }

            if (mapData.Length > 50000)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Game map size too large: {mapData.Length} bytes (expected < 50KB)";
                return result;
            }

            // 2. 检查数据模式（游戏地图通常包含特定的数值范围）
            // 例如：推箱子游戏中的地图可能使用 0-9 的数值表示不同元素
            int zeroCount = 0;
            int nonZeroCount = 0;

            for (int i = 0; i < mapData.Length; i++)
            {
                byte value = mapData[i];

                if (value == 0)
                {
                    zeroCount++;
                }
                else
                {
                    nonZeroCount++;
                }
            }

            // 如果全部是零或全部是非零，可能不是有效的地图
            if (zeroCount == mapData.Length || nonZeroCount == mapData.Length)
            {
                result.IsValid = false;
                result.ErrorMessage = "Game map contains uniform data (all zeros or all non-zeros), likely invalid";
                return result;
            }

            result.IsValid = true;
            result.Info = $"Valid game map: {mapData.Length} bytes, {zeroCount} zeros, {nonZeroCount} non-zeros";
            return result;
        }

        /// <summary>
        /// 获取游戏地图的显示文本
        /// </summary>
        public static string GetDisplayText(GameMapValidationResult result)
        {
            if (!result.IsValid)
            {
                return $"❌ Invalid Game Map\n{result.ErrorMessage}";
            }

            return $"✓ Valid Game Map\n{result.Info}";
        }
    }
}
