BOOL
ReadFromScsi(
HANDLE fileHandle,
int    cdbLen,
void  *cdb,
int    dataLen,
BYTE  *data//char  *data
);//发命令从Scsi读出
BOOL
WriteToScsi(
HANDLE fileHandle,
int    cdbLen,
void  *cdb,
int    dataLen,
BYTE  *data//char  *data
);//发命令向Scsi写入
