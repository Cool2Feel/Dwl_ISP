.\maxUsrRes.exe OSD_source.h OSD_source.bin user_icon.c user_icon.h
copy .\OSD_source.bin ..\resTable\OSD_source.bin
.\Platte_Tiga2Vison.exe .\palette.txt .\palette.bin
copy .\palette.bin ..\restable\palette.bin
.\maxUsrRes.exe .\OSD_source.h .\OSD_source.bin .\user_icon.c .\user_icon.h
copy .\user_icon.c .\..\user_icon.c
copy .\user_icon.h .\..\user_icon.h
copy .\r_palette.h .\..\r_palette.h