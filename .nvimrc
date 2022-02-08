lua << EOF
local pid = vim.fn.getpid()
local home = os.getenv("HOME")
local omnisharp_bin = home .. "/.local/share/omnisharp/run"

require'lspconfig'.omnisharp.setup {
    cmd = { omnisharp_bin, "-lsp", "-hpid", tostring(pid) },
    on_attach = require'completion'.on_attach
}
EOF

map <leader><F8> :Dispatch dotnet build --no-restore<CR>
map <leader><F5> :Dispatch ./ol/bin/Debug/net6.0/ol -S example/main.ol<CR>
map <leader><F10> :Dispatch ./example/bin/Example<CR>
map <leader>t<F5> :Dispatch ./ol/bin/Debug/net6.0/ol -S test/main.ol<CR>
map <leader>t<F10> :Dispatch ./test/bin/tests<CR>

au BufEnter *.cs :setlocal commentstring=//\ %s
au BufEnter *.ol :setlocal commentstring=//\ %s

call SetupSvn()
