OUTPUT_FORMAT(elf32-or1k)
OUTPUT_ARCH(or1k)
ENTRY(l1_func_spi_init)

MEMORY
{
  sdram (rwx) : ORIGIN = 0x020ccec0, LENGTH = 0x2000
}

SECTIONS
{
  . = 0x020ccec0;

  .text : { *(.text) *(.text.*) } > sdram
  .rodata : { *(.rodata) *(.rodata.*) } > sdram
  .data : { *(.data) *(.data.*) } > sdram
  .sdata : { *(.sdata) *(.sdata.*) } > sdram

  /DISCARD/ : {
    *(.comment) *(.note*) *(.debug*) *(.eh_frame) *(.reginfo)
  }
}

/*
 * 固件侧 ABS 符号（与本次配套的固件 build 对应，来源 order.ini / ax329x_sdk.map）：
 *   cb_mem2FIFO = 0x02044524
 */
cb_mem2FIFO = 0x02044524;
