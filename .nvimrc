lua << EOF
local pid = vim.fn.getpid()
local home = os.getenv("HOME")
local omnisharp_bin = home .. "/.local/share/omnisharp/run"

require'lspconfig'.omnisharp.setup {
    cmd = { omnisharp_bin, "-lsp", "-hpid", tostring(pid) }
}
EOF

map <leader>b<F8> :Dispatch dotnet build --no-restore<CR>
map <leader><F8> :Dispatch ./ol/bin/Debug/net6.0/ol ol2/main.ol<CR>
map <leader>t<F8> :Dispatch ./ol/bin/Debug/net6.0/ol -R -S ol2/main.ol<CR>
map <leader><F5> :Dispatch ./ol/bin/Debug/net6.0/ol -S example/main.ol<CR>
" map <leader><F5> :Dispatch ./ol2/bin/ol example/main.ol -noThreads<CR>
map <leader><F10> :Dispatch ./example/bin/Example<CR>
map <leader>t<F5> :Dispatch ./ol/bin/Debug/net6.0/ol -S test/main.ol<CR>
" map <leader>t<F5> :Dispatch ./ol2/bin/ol -S test/main.ol<CR>
map <leader>t<F10> :Dispatch ./test/bin/tests<CR>

au BufEnter *.cs :setlocal commentstring=//\ %s
au BufEnter *.ol :setlocal commentstring=//\ %s

if has('win32')
    call SetupSvn()
endif
