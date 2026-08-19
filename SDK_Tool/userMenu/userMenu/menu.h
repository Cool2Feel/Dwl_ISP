#ifndef  MENU_H
#define  MENU_H


#define MENU_OP_MAX      128
#define MENU_SOP_MAX     20
typedef struct MENU_INFO_S
{
	unsigned char option_id;          // option id,tells the option index
    unsigned char options;            // number of sub option	
	unsigned char option_type;       // option type/ normal,date,delete,format...
	unsigned char option_sub;        // the selected option index in sub-option
	
	unsigned int config_id;         // configure id,for set or get from firgure table

	unsigned int name;              // option name
	unsigned int icon;              // option icon

	unsigned int subname[MENU_SOP_MAX];       //sub option name
}MENU_INFO_T;
//----------user string
int user_stringInit(char *filename);

int user_stringFindStr(char *str);


int user_stringFindCfg(char *str);


int user_stringFindLan(char *str);






int user_menuInit(char *filename);


MENU_INFO_T *user_menuFind(int i);



int user_setting_init(char *filename);


#endif