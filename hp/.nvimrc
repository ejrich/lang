map <leader><F8> :Dispatch ~/lang/ol/bin/Debug/net6.0/ol hp.ol<CR>
map <leader>t<F8> :Dispatch ~/lang/ol/bin/Debug/net6.0/ol -R hp.ol<CR>

au BufEnter *.ol :setlocal commentstring=//\ %s
