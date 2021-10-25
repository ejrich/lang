map <leader><F8> :Dispatch ~/lang/ol/bin/Debug/net5.0/ol hp.ol<CR>
map <leader>t<F8> :Dispatch ~/lang/ol/bin/Debug/net5.0/ol -R hp.ol<CR>
map <leader><F5> :Dispatch ./bin/hp Xlib.h X11<CR>
map <leader>t<F5> :Dispatch ./bin/hp vulkan.h vulkan<CR>

au BufEnter *.ol :setlocal commentstring=//\ %s
