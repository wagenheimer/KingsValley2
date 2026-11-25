"""Leitura dos dados do King's Valley II (ROM MSX1 RC-760, mapper Konami SCC)."""
ROM = None
BASE = 0x14000  # banco 10 mapeado em 0x6000


def load(path="rom/kv2_msx1.rom"):
    global ROM
    ROM = open(path, "rb").read()
    return ROM


def cpu(addr):
    """Endereco de CPU (bancos 10.. mapeados a partir de 0x6000) -> offset na ROM."""
    return BASE + (addr - 0x6000)


def word(off):
    return ROM[off] | (ROM[off + 1] << 8)


def ptr(table, stage):
    return cpu(word(cpu(table) + 2 * (stage - 1)))


def terrain(stage):
    """Descomprime o terreno (RLE de celulas de 2 bits). Devolve lista de 3072 celulas."""
    p = ptr(0x6000, stage)
    cells = []
    while ROM[p]:
        n, v = ROM[p] & 0x3F, ROM[p] >> 6
        cells.extend([v] * n)
        p += 1
    return cells, p - ptr(0x6000, stage) + 1
