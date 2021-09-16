lua << EOF
local pid = vim.fn.getpid()
local home = os.getenv("HOME")
local omnisharp_bin = home .. "/.local/share/omnisharp/run"

require'lspconfig'.omnisharp.setup {
    cmd = { omnisharp_bin, "-lsp", "-hpid", tostring(pid) },
    on_attach = require'completion'.on_attach
}
EOF
