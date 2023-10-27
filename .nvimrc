map <leader><F8> :Dispatch dotnet build --no-restore<CR>
map <leader><F5> :Dispatch ./ol/bin/Debug/net6.0/ol -S example/main.ol<CR>
map <leader><F10> :Dispatch ./example/bin/Example<CR>
map <leader>t<F5> :Dispatch ./ol/bin/Debug/net6.0/ol -S test/main.ol<CR>
map <leader>t<F10> :Dispatch ./test/bin/tests<CR>

au BufEnter *.cs :setlocal commentstring=//\ %s
au BufEnter *.ol :setlocal commentstring=//\ %s

lua SetupP4(vim.fn.hostname() .. "-lang")
