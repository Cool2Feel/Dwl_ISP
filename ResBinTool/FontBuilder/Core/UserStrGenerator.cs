using System.Collections.Generic;
using System.IO;
using System.Text;
using FontBuilder.Models;

namespace FontBuilder.Core
{
    /// <summary>
    /// user_str.c / user_str.h 生成器
    ///
    /// user_str.h 包含两个枚举:
    ///   1. R_ID_STR_* 枚举（字符串 ID，起始 = R_ID_TYPE_STR = 0xE000）
    ///   2. CFG_* 枚举（配置 ID，起始 = 0）
    /// 以及一个语言表枚举（LANUAGE_*）
    ///
    /// user_str.c 包含 User_String_Table[] 数组：
    ///   {R_ID_STR_*, NULL, 0, 0, langId}
    /// 其中 langId 是 0x00000000 ~ 0x00000013 的语言位掩码
    /// </summary>
    public static class UserStrGenerator
    {
        /// <summary>
        /// 生成 user_str.h
        /// </summary>
        /// <param name="stringIdNames">字符串 ID 名称列表（如 STR_LAN_ENGLISH）</param>
        /// <param name="config">配置</param>
        /// <param name="outputPath">输出路径</param>
        public static void WriteHeader(List<string> stringIdNames, FontBuildConfig config, string outputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("/****************************************************************************");
            sb.AppendLine("       ***             ***                      MAXLIB-GRAPHC                  ");
            sb.AppendLine("      ** **           ** **                                                    ");
            sb.AppendLine("     **   **         **   **            THE MAXLIB FOR IMAGE SHOW PROCESS      ");
            sb.AppendLine("    **     **       **     **                                                  ");
            sb.AppendLine("   **       **     **       **              MAX ROURCE ICON MANAGEMENT         ");
            sb.AppendLine("  **         **   **         **                                                ");
            sb.AppendLine(" **           ** **           **              (C) COPYRIGHT 2016 MAX           ");
            sb.AppendLine("**             ***             **                                               ");
            sb.AppendLine("                                                                                ");
            sb.AppendLine("* File Name   : user_str.c                                                      ");
            sb.AppendLine("* Description : This file for maxlib resource str managemant                    ");
            sb.AppendLine("******************************************************************************/");
            sb.AppendLine();
            sb.AppendLine("#ifndef USER_STR_H");
            sb.AppendLine("   #define USER_STR_H");
            sb.AppendLine();
            sb.AppendLine("extern R_STRING_T User_String_Table[];");
            sb.AppendLine();

            // 枚举1: R_ID_STR_* (起始 = R_ID_TYPE_STR)
            // 注意：R_ID_TYPE_STR 在外部头文件定义，user_str.h 不重复定义
            // 第一项必须带 ` = R_ID_TYPE_STR` 初始化器（与参考文件一致）
            sb.AppendLine("enum");
            sb.AppendLine("{");
            for (int i = 0; i < stringIdNames.Count; i++)
            {
                string name = stringIdNames[i];
                if (!name.StartsWith("R_ID_")) name = "R_ID_" + name;
                string suffix = i == stringIdNames.Count - 1 ? "" : ",";
                string initializer = i == 0 ? " = R_ID_TYPE_STR" : "";
                sb.AppendLine($"   {name}{initializer}{suffix}");
            }
            sb.AppendLine("   R_STR_MAX");
            sb.AppendLine("};");
            sb.AppendLine();
            sb.AppendLine("#define STRID2CFGVALUE(strid)    (strid&0xff)");

            // 枚举2: CFG_* (起始 = 0)
            sb.AppendLine("enum");
            sb.AppendLine("{ // configure id table");
            sb.AppendLine("   CFG_LAN_ENGLISH=0,");
            for (int i = 1; i < stringIdNames.Count; i++)
            {
                string name = stringIdNames[i].Replace("R_ID_", "CFG_");
                string suffix = i == stringIdNames.Count - 1 ? "" : ",";
                sb.AppendLine($"   {name}{suffix}");
            }
            sb.AppendLine("   CFG_ID_MAX");
            sb.AppendLine("};");
            sb.AppendLine();

            // 语言表枚举（参考文件中第一项带 .txt 注释，其余仅 //）
            sb.AppendLine("enum // langauage table");
            sb.AppendLine("{");
            for (int i = 0; i < config.Languages.Count; i++)
            {
                var lang = config.Languages[i];
                string langName = lang.Name.ToUpperInvariant();
                if (i == 0)
                {
                    sb.AppendLine($"   LANUAGE_{langName} = {i},//{lang.Name}.txt");
                }
                else
                {
                    string suffix = i == config.Languages.Count - 1 ? "" : ",";
                    sb.AppendLine($"   LANUAGE_{langName}{suffix} //");
                }
            }
            sb.AppendLine("   LANUAGE_MAX");
            sb.AppendLine("};");
            sb.AppendLine();
            sb.AppendLine("#endif");

            File.WriteAllText(outputPath, sb.ToString(), Encoding.ASCII);
        }

        /// <summary>
        /// 生成 user_str.c
        /// </summary>
        public static void WriteSource(List<string> stringIdNames, FontBuildConfig config, string outputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("/****************************************************************************");
            sb.AppendLine("* File Name   : user_str.c                                                      ");
            sb.AppendLine("* Description : This file for maxlib resource str managemant                    ");
            sb.AppendLine("******************************************************************************/");
            sb.AppendLine();
            sb.AppendLine("#include \"../application.h\"");
            sb.AppendLine("#include \"user_str.h\"");
            sb.AppendLine();
            sb.AppendLine($"R_STRING_T User_String_Table[R_STR_MAX&0xffff] = ");
            sb.AppendLine("{");

            for (int i = 0; i < stringIdNames.Count; i++)
            {
                string name = stringIdNames[i];
                if (!name.StartsWith("R_ID_")) name = "R_ID_" + name;
                // 第 5 字段（langMask）就是顺序索引 0,1,2,...（已通过 AnalyzeFontBin.ps1 验证）
                uint langMask = (uint)i;
                sb.AppendLine($"   {{{name,-32},  (void *)0,0,0,0x{langMask:X8}}},");
            }
            sb.AppendLine("};");

            File.WriteAllText(outputPath, sb.ToString(), Encoding.ASCII);
        }
    }
}
