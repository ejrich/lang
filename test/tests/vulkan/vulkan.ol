#import standard
#import compiler
#import vulkan
#import math

// This test follows vulkan-tutorial.com

main() {
    create_window();

    init_vulkan();

    while true {
        if !handle_inputs() break;

        draw_frame();
    }

    vkDeviceWaitIdle(device);

    cleanup();
}

init_vulkan() {
    create_instance();

    setup_debug_messenger();

    create_surface();

    pick_physical_device();

    create_logical_device();

    create_swap_chain();

    create_image_views();

    create_render_pass();

    create_descriptor_set_layout();

    create_graphics_pipeline();

    create_command_pool();

    create_color_resources();

    create_depth_resources();

    create_framebuffers();

    texture_mip_levels := create_texture_image("test/tests/vulkan/textures/statue.jpg", &texture_image, &texture_image_memory);

    model_mip_levels := create_texture_image("test/tests/vulkan/textures/viking_room.png", &model_texture_image, &model_texture_image_memory);

    create_texture_image_view(texture_image, &texture_image_view, texture_mip_levels);

    create_texture_image_view(model_texture_image, &model_texture_image_view, model_mip_levels);

    create_texture_sampler();

    setup_vertices();

    load_model();

    create_vertex_buffer(vertices, &vertex_buffer, &vertex_buffer_memory);

    create_index_buffer(indices, &index_buffer, &index_buffer_memory);

    create_uniform_buffers();

    create_descriptor_pool();

    create_descriptor_sets(&descriptor_sets, texture_image_view);

    create_descriptor_sets(&model_descriptor_sets, model_texture_image_view);

    create_command_buffers();

    create_sync_objects();

    start = get_performance_counter();
}


// Part 1: https://vulkan-tutorial.com/Drawing_a_triangle/Setup/Instance
instance: VkInstance*;

create_instance() {
    if enable_validation_layers && !check_validation_layer_support() {
        print("Validation layers requested, but not available\n");
        exit_program(1);
    }

    version := vk_make_api_version(0, 1, 0, 0);
    application_name := "Vulkan Test"; #const
    engine_name := "Vulkan Engine Test"; #const

    app_info: VkApplicationInfo = {
        pApplicationName = application_name.data;
        applicationVersion = version;
        pEngineName = engine_name.data;
        engineVersion = version;
        apiVersion = vk_api_version_1_0();
    }

    extensions := get_required_extensions();

    instance_create_info: VkInstanceCreateInfo = {
        pApplicationInfo = &app_info;
        enabledExtensionCount = extensions.length;
        ppEnabledExtensionNames = extensions.data;
    }

    if enable_validation_layers {
        instance_create_info.enabledLayerCount = validation_layers.length;
        instance_create_info.ppEnabledLayerNames = &validation_layers[0].data; // Not pretty, but works
    }

    print("Creating vulkan instance\n");
    result := vkCreateInstance(&instance_create_info, null, &instance);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create vulkan instance %\n", result);
        exit_program(1);
    }
}

Array<u8*> get_required_extensions() {
    extension_count: u32;
    result := vkEnumerateInstanceExtensionProperties(null, &extension_count, null);
    if result != VkResult.VK_SUCCESS {
        print("Unable to get vulkan extensions\n");
        exit_program(1);
    }

    extensions: Array<VkExtensionProperties>[extension_count];
    vkEnumerateInstanceExtensionProperties(null, &extension_count, extensions.data);

    extension_names: Array<u8*>;
    each extension, i in extensions {
        name := convert_c_string(&extension.extensionName);
        print("Extension - %\n", name);

        #if os == OS.Linux {
            if name == VK_KHR_SURFACE_EXTENSION_NAME {
                array_insert(&extension_names, VK_KHR_SURFACE_EXTENSION_NAME.data);
            }
            else if name == VK_KHR_XLIB_SURFACE_EXTENSION_NAME {
                array_insert(&extension_names, VK_KHR_XLIB_SURFACE_EXTENSION_NAME.data);
            }
        }
        #if os == OS.Windows {
            if name == VK_KHR_SURFACE_EXTENSION_NAME {
                array_insert(&extension_names, VK_KHR_SURFACE_EXTENSION_NAME.data);
            }
            else if name == VK_KHR_WIN32_SURFACE_EXTENSION_NAME {
                array_insert(&extension_names, VK_KHR_WIN32_SURFACE_EXTENSION_NAME.data);
            }
        }
    }

    if enable_validation_layers {
        array_insert(&extension_names, VK_EXT_DEBUG_UTILS_EXTENSION_NAME .data);
    }

    return extension_names;
}

cleanup() {
    cleanup_swap_chain();

    each i in 0..MAX_FRAMES_IN_FLIGHT-1 {
        vkDestroySemaphore(device, image_available_semaphores[i], null);
        vkDestroySemaphore(device, render_finished_semaphores[i], null);
        vkDestroyFence(device, in_flight_fences[i], null);
    }

    vkDestroySampler(device, texture_sampler, null);
    vkDestroyDescriptorSetLayout(device, descriptor_set_layout, null);

    vkDestroyImage(device, texture_image, null);
    vkFreeMemory(device, texture_image_memory, null);
    vkDestroyImageView(device, texture_image_view, null);

    vkDestroyImage(device, model_texture_image, null);
    vkFreeMemory(device, model_texture_image_memory, null);
    vkDestroyImageView(device, model_texture_image_view, null);

    vkDestroyBuffer(device, index_buffer, null);
    vkFreeMemory(device, index_buffer_memory, null);

    vkDestroyBuffer(device, vertex_buffer, null);
    vkFreeMemory(device, vertex_buffer_memory, null);

    vkDestroyBuffer(device, model_index_buffer, null);
    vkFreeMemory(device, model_index_buffer_memory, null);

    vkDestroyBuffer(device, model_vertex_buffer, null);
    vkFreeMemory(device, model_vertex_buffer_memory, null);

    vkDestroyCommandPool(device, command_pool, null);
    vkDestroyDevice(device, null);
    vkDestroySurfaceKHR(instance, surface, null);

    if enable_validation_layers {
        func: PFN_vkDestroyDebugUtilsMessengerEXT = vkGetInstanceProcAddr(instance, "vkDestroyDebugUtilsMessengerEXT");
        if func != null {
            func(instance, debug_messenger, null);
        }
    }

    vkDestroyInstance(instance, null);

    close_window();
}


// Part 2: https://vulkan-tutorial.com/Drawing_a_triangle/Setup/Validation_layers
enable_validation_layers := true; #const
validation_layers: Array<string> = ["VK_LAYER_KHRONOS_validation"]

bool check_validation_layer_support() {
    layer_count: u32;
    vkEnumerateInstanceLayerProperties(&layer_count, null);

    available_layers: Array<VkLayerProperties>[layer_count];
    vkEnumerateInstanceLayerProperties(&layer_count, available_layers.data);

    each layer_name in validation_layers {
        layer_found := false;

        each layer_properties in available_layers {
            name := convert_c_string(&layer_properties.layerName);
            if layer_name == name {
                layer_found = true;
                break;
            }
        }

        if !layer_found return false;
    }

    return true;
}

debug_messenger: VkDebugUtilsMessengerEXT*;

setup_debug_messenger() {
    if !enable_validation_layers return;

    messenger_create_info: VkDebugUtilsMessengerCreateInfoEXT = {
        messageSeverity = VkDebugUtilsMessageSeverityFlagBitsEXT.VK_DEBUG_UTILS_MESSAGE_SEVERITY_NOT_INFO_BIT_EXT;
        messageType = VkDebugUtilsMessageTypeFlagBitsEXT.VK_DEBUG_UTILS_MESSAGE_TYPE_ALL_EXT;
        pfnUserCallback = debug_callback;
    }

    func: PFN_vkCreateDebugUtilsMessengerEXT = vkGetInstanceProcAddr(instance, "vkCreateDebugUtilsMessengerEXT");
    if func != null {
        result := func(instance, &messenger_create_info, null, &debug_messenger);
        if result != VkResult.VK_SUCCESS {
            print("Failed to set up debug messenger %\n", result);
            exit_program(1);
        }
    }
    else {
        print("Failed to set up debug messenger\n");
        exit_program(1);
    }
}

u32 debug_callback(VkDebugUtilsMessageSeverityFlagBitsEXT severity, VkDebugUtilsMessageTypeFlagBitsEXT type, VkDebugUtilsMessengerCallbackDataEXT* callback_data, void* user_data) {
    if severity == VkDebugUtilsMessageSeverityFlagBitsEXT.VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT {
        print("Warning - %\n", convert_c_string(callback_data.pMessage));
    }
    else if severity == VkDebugUtilsMessageSeverityFlagBitsEXT.VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT {
        print("Error - %\n", convert_c_string(callback_data.pMessage));
    }

    return VK_FALSE;
}


// Part 3: https://vulkan-tutorial.com/Drawing_a_triangle/Setup/Physical_devices_and_queue_families
physical_device: VkPhysicalDevice*;

pick_physical_device() {
    device_count: u32;
    vkEnumeratePhysicalDevices(instance, &device_count, null);

    if device_count == 0 {
        print("Failed to find GPUs with Vulkan support\n");
        exit_program(1);
    }

    devices: Array<VkPhysicalDevice*>[device_count];
    vkEnumeratePhysicalDevices(instance, &device_count, devices.data);

    highest_score: int;
    each device_candidate in devices {
        score := is_device_suitable(device_candidate);
        if score > highest_score {
            physical_device = device_candidate;
            highest_score = score;
            break;
        }
    }

    if physical_device == null {
        print("Failed to find a suitable GPU\n");
        exit_program(1);
    }

    msaa_samples = get_max_usable_sample_count();
}

int is_device_suitable(VkPhysicalDevice* device) {
    properties: VkPhysicalDeviceProperties;
    vkGetPhysicalDeviceProperties(device, &properties);

    features: VkPhysicalDeviceFeatures;
    vkGetPhysicalDeviceFeatures(device, &features);

    score := 0;

    if properties.deviceType == VkPhysicalDeviceType.VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU score += 1000;

    score += properties.limits.maxImageDimension2D;

    _: u32;
    if features.geometryShader == VK_FALSE score = 0;
    else if !find_queue_families(device, &_, &_) score = 0;
    else if !check_device_extension_support(device) score = 0;
    else if !swap_chain_adequate(device) score = 0;
    else if features.samplerAnisotropy == VK_FALSE score = 0;

    print("Device - %, Score = %\n", convert_c_string(&properties.deviceName), score);

    return score;
}

bool find_queue_families(VkPhysicalDevice* device, u32* graphics_family, u32* present_family) {
    queue_family_count: u32;
    vkGetPhysicalDeviceQueueFamilyProperties(device, &queue_family_count, null);

    families: Array<VkQueueFamilyProperties>[queue_family_count];
    vkGetPhysicalDeviceQueueFamilyProperties(device, &queue_family_count, families.data);

    graphics_family_found: bool;
    present_support: u32;
    each family, i in families {
        if family.queueFlags & VkQueueFlagBits.VK_QUEUE_GRAPHICS_BIT {
            *graphics_family = i;
            graphics_family_found = true;
        }

        if present_support == VK_FALSE {
            vkGetPhysicalDeviceSurfaceSupportKHR(device, i, surface, &present_support);

            if present_support {
                *present_family = i;
            }
        }

        if graphics_family_found && present_support == VK_TRUE {
            return true;
        }
    }

    return false;
}


// Part 5: https://vulkan-tutorial.com/Drawing_a_triangle/Setup/Logical_device_and_queues
device: VkDevice*;
graphics_queue: VkQueue*;

create_logical_device() {
    features: VkPhysicalDeviceFeatures = {
        samplerAnisotropy = VK_TRUE;
        sampleRateShading = VK_TRUE;
    }
    vkGetPhysicalDeviceFeatures(physical_device, &features);

    device_create_info: VkDeviceCreateInfo = {
        enabledExtensionCount = device_extensions.length;
        ppEnabledExtensionNames = &device_extensions[0].data; // Not pretty, but works for now
        pEnabledFeatures = &features;
    }

    graphics_family, present_family: u32;
    find_queue_families(physical_device, &graphics_family, &present_family);

    queuePriority := 1.0;
    queue_create_info: VkDeviceQueueCreateInfo = {
        queueFamilyIndex = graphics_family;
        queueCount = 1;
        pQueuePriorities = &queuePriority;
    }

    if graphics_family == present_family {
        device_create_info.queueCreateInfoCount = 1;
        device_create_info.pQueueCreateInfos = &queue_create_info;
    }
    else {
        queue_create_infos: Array<VkDeviceQueueCreateInfo>[2];
        queue_create_infos[0] = queue_create_info;

        queue_create_info.queueFamilyIndex = present_family;
        queue_create_infos[1] = queue_create_info;

        device_create_info.queueCreateInfoCount = 2;
        device_create_info.pQueueCreateInfos = queue_create_infos.data;
    }

    if enable_validation_layers {
        device_create_info.enabledLayerCount = validation_layers.length;
        device_create_info.ppEnabledLayerNames = &validation_layers[0].data; // Not pretty, but works
    }

    result := vkCreateDevice(physical_device, &device_create_info, null, &device);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create vulkan device %\n", result);
        exit_program(1);
    }

    vkGetDeviceQueue(device, graphics_family, 0, &graphics_queue);
    vkGetDeviceQueue(device, present_family, 0, &present_queue);
}


// Part 6: https://vulkan-tutorial.com/en/Drawing_a_triangle/Presentation/Window_surface
surface: VkSurfaceKHR*;
present_queue: VkQueue*;

create_surface() {
    result: VkResult;

    #if os == OS.Linux {
        surface_create_info: VkXlibSurfaceCreateInfoKHR = {
            dpy = window.handle;
            window = window.window;
        }

        result = vkCreateXlibSurfaceKHR(instance, &surface_create_info, null, &surface);
    }
    #if os == OS.Windows {
        surface_create_info: VkWin32SurfaceCreateInfoKHR = {
            hinstance = GetModuleHandleA(null);
            hwnd = window.handle;
        }

        result = vkCreateWin32SurfaceKHR(instance, &surface_create_info, null, &surface);
    }

    if result != VkResult.VK_SUCCESS {
        print("Unable to create window surface %\n", result);
        exit_program(1);
    }
}

struct Window {
    handle: void*;
    window: u64;
    graphics_context: void*;
    width: u32 = 960;
    height: u32 = 720;
}

window: Window;

create_window() {
    window_name := "Vulkan Window"; #const
    #if os == OS.Linux {
        XInitThreads();

        display := XOpenDisplay(null);
        screen := XDefaultScreen(display);
        black := XBlackPixel(display, screen);
        white := XWhitePixel(display, screen);

        default_window := XDefaultRootWindow(display);
        x_win := XCreateSimpleWindow(display, default_window, 0, 0, window.width, window.height, 0, white, black);
        XSetStandardProperties(display, x_win, window_name, "", 0, null, 0, null);

        XSelectInput(display, x_win, XInputMasks.ButtonPressMask|XInputMasks.KeyPressMask|XInputMasks.ExposureMask|XInputMasks.StructureNotifyMask);

        gc := XCreateGC(display, x_win, 0, null);

        XSetBackground(display, gc, white);
        XSetForeground(display, gc, black);

        XClearWindow(display, x_win);
        XMapRaised(display, x_win);

        window.handle = display;
        window.window = x_win; window.graphics_context = gc;
    }
    #if os == OS.Windows {
        class_name := "VulkanWindowClass";

        hinstance := GetModuleHandleA(null);
        window_class: WNDCLASSEXA = { cbSize = size_of(WNDCLASSEXA); style = WindowClassStyle.CS_VREDRAW | WindowClassStyle.CS_HREDRAW; lpfnWndProc = handle_window_inputs; hInstance = hinstance; lpszClassName = class_name.data; }
        RegisterClassExA(&window_class);

        window.handle = CreateWindowExA(0, class_name, window_name, WindowStyle.WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, window.width, window.height, null, null, hinstance, null);
        ShowWindow(window.handle, WindowShow.SW_NORMAL);
        UpdateWindow(window.handle);
    }
}

#if os == OS.Windows {
    running := true;

    s64 handle_window_inputs(Handle* handle, MessageType message, u64 wParam, s64 lParam) {
        result: s64 = 0;

        if message == MessageType.WM_CLOSE {
            running = false;
        }
        else if message == MessageType.WM_KEYDOWN {
            if wParam == 'Q' {
                running = false;
            }
        }
        else if message == MessageType.WM_SIZE {
            window.width = lParam & 0xFFFF;
            window.height = (lParam & 0xFFFF0000) >> 16;
            framebuffer_resized = true;
        }
        else {
            result = DefWindowProcA(handle, message, wParam, lParam);
        }

        return result;
    }
}

bool handle_inputs() {
    #if os == OS.Linux {
        while XPending(window.handle) {
            event: XEvent;

            XNextEvent(window.handle, &event);

            if event.type == XEventType.KeyPress {
                text: CArray<u8>[255];
                key: u64;

                if XLookupString(&event.xkey, &text, 255, &key, null) == 1 {
                    if text[0] == 'q' {
                        return false;
                    }
                }
            }
            else if event.type == XEventType.ConfigureNotify {
                if window.width != event.xconfigure.width || window.height != event.xconfigure.height {
                    window.width = event.xconfigure.width;
                    window.height = event.xconfigure.height;
                    framebuffer_resized = true;
                }
            }
        }
    }
    #if os == OS.Windows {
        message: MSG;

        while PeekMessageA(&message, null, 0, 0, RemoveMsg.PM_REMOVE) {
            if message.message == MessageType.WM_QUIT {
                print("Quitting\n");
                return false;
            }

            TranslateMessage(&message);
            DispatchMessageA(&message);
        }

        if !running return false;
    }

    // return true;
    return false;
}

close_window() {
    #if os == OS.Linux {
        XFreeGC(window.handle, window.graphics_context);
        XDestroyWindow(window.handle, window.window);
        XCloseDisplay(window.handle);
    }
    #if os == OS.Windows {
        CloseWindow(window.handle);
    }
}


// Part 7: https://vulkan-tutorial.com/en/Drawing_a_triangle/Presentation/Swap_chain
device_extensions: Array<string> = ["VK_KHR_swapchain"]

bool check_device_extension_support(VkPhysicalDevice* device) {
    extension_count: u32;
    vkEnumerateDeviceExtensionProperties(device, null, &extension_count, null);

    available_extensions: Array<VkExtensionProperties>[extension_count];
    vkEnumerateDeviceExtensionProperties(device, null, &extension_count, available_extensions.data);

    each required_extension in device_extensions {
        found := false;

        each extension in available_extensions {
            name := convert_c_string(&extension.extensionName);

            if name == required_extension {
                found = true;
                break;
            }
        }

        if !found return false;
    }

    return true;
}

struct SwapChainSupportDetails {
    capabilities: VkSurfaceCapabilitiesKHR;
    formats: Array<VkSurfaceFormatKHR>;
    present_modes: Array<VkPresentModeKHR>;
}

SwapChainSupportDetails query_swap_chain_support(VkPhysicalDevice* device) {
    capabilities: VkSurfaceCapabilitiesKHR;
    vkGetPhysicalDeviceSurfaceCapabilitiesKHR(device, surface, &capabilities);

    format_count: u32;
    vkGetPhysicalDeviceSurfaceFormatsKHR(device, surface, &format_count, null);

    formats: Array<VkSurfaceFormatKHR>[format_count];
    vkGetPhysicalDeviceSurfaceFormatsKHR(device, surface, &format_count, formats.data);

    present_mode_count: u32;
    vkGetPhysicalDeviceSurfacePresentModesKHR(device, surface, &present_mode_count, null);

    present_modes: Array<VkPresentModeKHR>[present_mode_count];
    vkGetPhysicalDeviceSurfacePresentModesKHR(device, surface, &present_mode_count, present_modes.data);

    details: SwapChainSupportDetails = {
        capabilities = capabilities;
        formats = formats;
        present_modes = present_modes;
    }
    return details;
}

bool swap_chain_adequate(VkPhysicalDevice* device) {
    details := query_swap_chain_support(device);

    return details.formats.length > 0 && details.present_modes.length > 0;
}

swap_chain: VkSwapchainKHR*;
swap_chain_images: Array<VkImage*>;
swap_chain_format: VkFormat;
swap_chain_extent: VkExtent2D;

create_swap_chain() {
    details := query_swap_chain_support(physical_device);

    format := choose_swap_surface_format(details.formats);
    swap_chain_format = format.format;
    present_mode := choose_swap_present_mode(details.present_modes);
    swap_chain_extent = choose_swap_extent(details.capabilities);

    image_count: u32 = details.capabilities.minImageCount + 1;

    if details.capabilities.maxImageCount > 0 && image_count > details.capabilities.maxImageCount
        image_count = details.capabilities.maxImageCount;

    swapchain_create_info: VkSwapchainCreateInfoKHR = {
        surface = surface;
        minImageCount = image_count;
        imageFormat = format.format;
        imageColorSpace = format.colorSpace;
        imageExtent = swap_chain_extent;
        imageArrayLayers = 1;
        imageUsage = VkImageUsageFlagBits.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
        preTransform = details.capabilities.currentTransform;
        compositeAlpha = VkCompositeAlphaFlagBitsKHR.VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
        presentMode = present_mode;
        clipped = VK_TRUE;
    }

    graphics_family, present_family: u32;
    find_queue_families(physical_device, &graphics_family, &present_family);

    if graphics_family == present_family {
        swapchain_create_info.imageSharingMode = VkSharingMode.VK_SHARING_MODE_EXCLUSIVE;
    }
    else {
        queue_family_indices: CArray<u32> = [graphics_family, present_family]

        swapchain_create_info.imageSharingMode = VkSharingMode.VK_SHARING_MODE_CONCURRENT;
        swapchain_create_info.queueFamilyIndexCount = 2;
        swapchain_create_info.pQueueFamilyIndices = &queue_family_indices;
    }

    result := vkCreateSwapchainKHR(device, &swapchain_create_info, null, &swap_chain);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create swap chain %\n", result);
        exit_program(1);
    }

    vkGetSwapchainImagesKHR(device, swap_chain, &image_count, null);

    array_reserve(&swap_chain_images, image_count);
    vkGetSwapchainImagesKHR(device, swap_chain, &image_count, swap_chain_images.data);
}

VkSurfaceFormatKHR choose_swap_surface_format(Array<VkSurfaceFormatKHR> available_formats) {
    each format in available_formats {
        if format.format == VkFormat.VK_FORMAT_B8G8R8A8_SRGB && format.colorSpace == VkColorSpaceKHR.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR return format;
    }

    return available_formats[0];
}

VkPresentModeKHR choose_swap_present_mode(Array<VkPresentModeKHR> available_modes) {
    each mode in available_modes {
        if mode == VkPresentModeKHR.VK_PRESENT_MODE_MAILBOX_KHR return mode;
    }

    return VkPresentModeKHR.VK_PRESENT_MODE_FIFO_KHR;
}

VkExtent2D choose_swap_extent(VkSurfaceCapabilitiesKHR capabilities) {
    if capabilities.currentExtent.width != 0xFFFFFFFF {
        return capabilities.currentExtent;
    }

    width, height: int;
    extent: VkExtent2D;
    #if os == OS.Linux {
        attributes: XWindowAttributes;
        XGetWindowAttributes(window.handle, window.window, &attributes);

        extent.width = attributes.width;
        extent.height = attributes.height;
    }
    #if os == OS.Windows {
        rect: RECT;
        GetWindowRect(window.handle, &rect);

        extent.width = rect.right - rect.left;
        extent.height = rect.top - rect.bottom;
    }

    extent.width = clamp(extent.width, capabilities.minImageExtent.width, capabilities.maxImageExtent.width);
    extent.height = clamp(extent.height, capabilities.minImageExtent.height, capabilities.maxImageExtent.height);

    return extent;
}

u32 clamp(u32 value, u32 min, u32 max) {
    if value < min return min;
    if value > max return max;
    return value;
}


// Part 8: https://vulkan-tutorial.com/en/Drawing_a_triangle/Presentation/Image_views
swap_chain_image_views: Array<VkImageView*>;

create_image_views() {
    array_reserve(&swap_chain_image_views, swap_chain_images.length);

    view_create_info: VkImageViewCreateInfo = {
        viewType = VkImageViewType.VK_IMAGE_VIEW_TYPE_2D;
        format = swap_chain_format;
    }
    view_create_info.subresourceRange = {
        aspectMask = VkImageAspectFlagBits.VK_IMAGE_ASPECT_COLOR_BIT;
        baseMipLevel = 0;
        levelCount = 1;
        baseArrayLayer = 0;
        layerCount = 1;
    }

    each image, i in swap_chain_images {
        view_create_info.image = image;

        result := vkCreateImageView(device, &view_create_info, null, &swap_chain_image_views[i]);
        if result != VkResult.VK_SUCCESS {
            print("Unable to create image view %\n", result);
            exit_program(1);
        }
    }
}


// Part 9: https://vulkan-tutorial.com/en/Drawing_a_triangle/Graphics_pipeline_basics/Shader_modules
pipeline_layout: VkPipelineLayout*;
graphics_pipeline: VkPipeline*;

create_graphics_pipeline() {
    vertex_shader := create_shader_module("test/tests/vulkan/shaders/vert.spv");
    fragment_shader := create_shader_module("test/tests/vulkan/shaders/frag.spv");

    vertex_shader_stage_info: VkPipelineShaderStageCreateInfo = {
        stage = VkShaderStageFlagBits.VK_SHADER_STAGE_VERTEX_BIT;
        module = vertex_shader;
        pName = shader_entrypoint.data;
    }

    fragment_shader_stage_info: VkPipelineShaderStageCreateInfo = {
        stage = VkShaderStageFlagBits.VK_SHADER_STAGE_FRAGMENT_BIT;
        module = fragment_shader;
        pName = shader_entrypoint.data;
    }


    binding_description := get_binding_description();

    attribute_descriptions: Array<VkVertexInputAttributeDescription>[3];

    attribute_descriptions[0].binding = 0;
    attribute_descriptions[0].location = 0;
    attribute_descriptions[0].format = VkFormat.VK_FORMAT_R32G32B32_SFLOAT;
    attribute_descriptions[0].offset = 0;

    attribute_descriptions[1].binding = 0;
    attribute_descriptions[1].location = 1;
    attribute_descriptions[1].format = VkFormat.VK_FORMAT_R32G32B32_SFLOAT;
    attribute_descriptions[1].offset = size_of(Vector3);

    attribute_descriptions[2].binding = 0;
    attribute_descriptions[2].location = 2;
    attribute_descriptions[2].format = VkFormat.VK_FORMAT_R32G32_SFLOAT;
    attribute_descriptions[2].offset = size_of(Vector3) * 2;


    // Part 10: https://vulkan-tutorial.com/Drawing_a_triangle/Graphics_pipeline_basics/Fixed_functions
    vertex_input_info: VkPipelineVertexInputStateCreateInfo = {
        vertexBindingDescriptionCount = 1;
        pVertexBindingDescriptions = &binding_description;
        vertexAttributeDescriptionCount = attribute_descriptions.length;
        pVertexAttributeDescriptions = attribute_descriptions.data;
    }

    input_assembly: VkPipelineInputAssemblyStateCreateInfo = {
        topology = VkPrimitiveTopology.VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
        primitiveRestartEnable = VK_FALSE;
    }

    viewport: VkViewport = {
        x = 0.0;
        y = 0.0;
        width = cast(float, swap_chain_extent.width);
        height = cast(float, swap_chain_extent.height);
        minDepth = 0.0;
        maxDepth = 1.0;
    }

    scissor: VkRect2D = {
        extent = swap_chain_extent;
    }

    viewport_state: VkPipelineViewportStateCreateInfo = {
        viewportCount = 1;
        pViewports = &viewport;
        scissorCount = 1;
        pScissors = &scissor;
    }

    rasterizer: VkPipelineRasterizationStateCreateInfo = {
        depthClampEnable = VK_FALSE;
        rasterizerDiscardEnable = VK_FALSE;
        polygonMode = VkPolygonMode.VK_POLYGON_MODE_FILL;
        lineWidth = 1.0;
        cullMode = VkCullModeFlagBits.VK_CULL_MODE_BACK_BIT;
        frontFace = VkFrontFace.VK_FRONT_FACE_COUNTER_CLOCKWISE;
        depthBiasEnable = VK_FALSE;
        depthBiasConstantFactor = 0.0; // Optional
        depthBiasClamp = 0.0;          // Optional
        depthBiasSlopeFactor = 0.0;    // Optional
    }

    multisampling: VkPipelineMultisampleStateCreateInfo = {
        rasterizationSamples = msaa_samples;
        sampleShadingEnable = VK_TRUE;
        minSampleShading = 0.2;           // Optional
        pSampleMask = null;               // Optional
        alphaToCoverageEnable = VK_FALSE; // Optional
        alphaToOneEnable = VK_FALSE;      // Optional
    }

    color_blend_attachment: VkPipelineColorBlendAttachmentState = {
        colorWriteMask = VkColorComponentFlagBits.VK_COLOR_COMPONENT_RGBA;
        blendEnable = VK_FALSE;
        srcColorBlendFactor = VkBlendFactor.VK_BLEND_FACTOR_ONE;
        dstColorBlendFactor = VkBlendFactor.VK_BLEND_FACTOR_ZERO;
        colorBlendOp = VkBlendOp.VK_BLEND_OP_ADD;
        srcAlphaBlendFactor = VkBlendFactor.VK_BLEND_FACTOR_ONE;
        dstAlphaBlendFactor = VkBlendFactor.VK_BLEND_FACTOR_ZERO;
        alphaBlendOp = VkBlendOp.VK_BLEND_OP_ADD;
    }

    color_blending: VkPipelineColorBlendStateCreateInfo = {
        logicOpEnable = VK_FALSE;
        logicOp = VkLogicOp.VK_LOGIC_OP_COPY;
        attachmentCount = 1;
        pAttachments = &color_blend_attachment;
    }

    dynamic_states: Array<VkDynamicState> = [VkDynamicState.VK_DYNAMIC_STATE_VIEWPORT, VkDynamicState.VK_DYNAMIC_STATE_LINE_WIDTH]

    dynamic_state: VkPipelineDynamicStateCreateInfo = {
        dynamicStateCount = dynamic_states.length;
        pDynamicStates = dynamic_states.data;
    }

    pipeline_layout_info: VkPipelineLayoutCreateInfo = {
        setLayoutCount = 1;
        pSetLayouts = &descriptor_set_layout;
        pushConstantRangeCount = 0; // Optional
        pPushConstantRanges = null; // Optional
    }

    result := vkCreatePipelineLayout(device, &pipeline_layout_info, null, &pipeline_layout);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create pipeline layout %\n", result);
        exit_program(1);
    }

    depth_stencil: VkPipelineDepthStencilStateCreateInfo = {
        depthTestEnable = VK_TRUE;
        depthWriteEnable = VK_TRUE;
        depthCompareOp = VkCompareOp.VK_COMPARE_OP_LESS;
        depthBoundsTestEnable = VK_FALSE;
        minDepthBounds = 0.0; // Optional
        maxDepthBounds = 1.0; // Optional
        stencilTestEnable = VK_FALSE;
    }

    // Part 12: https://vulkan-tutorial.com/en/Drawing_a_triangle/Graphics_pipeline_basics/Conclusion
    shader_stages: Array<VkPipelineShaderStageCreateInfo> = [vertex_shader_stage_info, fragment_shader_stage_info]

    pipeline_info: VkGraphicsPipelineCreateInfo = {
        stageCount = shader_stages.length;
        pStages = shader_stages.data;
        pVertexInputState = &vertex_input_info;
        pInputAssemblyState = &input_assembly;
        pViewportState = &viewport_state;
        pRasterizationState = &rasterizer;
        pMultisampleState = &multisampling;
        pDepthStencilState = &depth_stencil;
        pColorBlendState = &color_blending;
        pDynamicState = null;
        layout = pipeline_layout;
        renderPass = render_pass;
        subpass = 0;
        basePipelineHandle = null;
        basePipelineIndex = -1;
    }

    result = vkCreateGraphicsPipelines(device, null, 1, &pipeline_info, null, &graphics_pipeline);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create graphics pipeline %\n", result);
        exit_program(1);
    }


    vkDestroyShaderModule(device, vertex_shader, null);
    vkDestroyShaderModule(device, fragment_shader, null);
}

shader_entrypoint := "main";

VkShaderModule* create_shader_module(string file) {
    found, code := read_file(file);

    if !found return null;

    shader_create_info: VkShaderModuleCreateInfo = {
        codeSize = code.length;
        pCode = cast(u32*, code.data);
    }

    shader_module: VkShaderModule*;
    result := vkCreateShaderModule(device, &shader_create_info, null, &shader_module);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create shader module %\n", result);
        exit_program(1);
    }

    return shader_module;
}


// Part 11: https://vulkan-tutorial.com/Drawing_a_triangle/Graphics_pipeline_basics/Render_passes
render_pass: VkRenderPass*;

create_render_pass() {
    color_attachment: VkAttachmentDescription = {
        format = swap_chain_format;
        samples = msaa_samples;
        loadOp = VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_CLEAR;
        storeOp = VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_STORE;
        stencilLoadOp = VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        stencilStoreOp = VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_DONT_CARE;
        initialLayout = VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED;
        finalLayout = VkImageLayout.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    }

    depth_attachment: VkAttachmentDescription = {
        format = find_depth_format();
        samples = msaa_samples;
        loadOp = VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_CLEAR;
        storeOp = VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_DONT_CARE;
        stencilLoadOp = VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        stencilStoreOp = VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_DONT_CARE;
        initialLayout = VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED;
        finalLayout = VkImageLayout.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
    }

    color_attachment_resolve: VkAttachmentDescription = {
        format = swap_chain_format;
        samples = VkSampleCountFlagBits.VK_SAMPLE_COUNT_1_BIT;
        loadOp = VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        storeOp = VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_STORE;
        stencilLoadOp = VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        stencilStoreOp = VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_DONT_CARE;
        initialLayout = VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED;
        finalLayout = VkImageLayout.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
    }

    attachments: Array<VkAttachmentDescription> = [color_attachment, depth_attachment, color_attachment_resolve]

    color_attachment_ref: VkAttachmentReference = {
        attachment = 0;
        layout = VkImageLayout.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    }

    depth_attachment_ref: VkAttachmentReference = {
        attachment = 1;
        layout = VkImageLayout.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
    }

    color_attachment_resolve_ref: VkAttachmentReference = {
        attachment = 2;
        layout = VkImageLayout.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    }

    subpass: VkSubpassDescription = {
        pipelineBindPoint = VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_GRAPHICS;
        colorAttachmentCount = 1;
        pColorAttachments = &color_attachment_ref;
        pResolveAttachments = &color_attachment_resolve_ref;
        pDepthStencilAttachment = &depth_attachment_ref;
    }

    dependency: VkSubpassDependency = {
        srcSubpass = VK_SUBPASS_EXTERNAL;
        dstSubpass = 0;
        srcStageMask = VkPipelineStageFlagBits.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT | VkPipelineStageFlagBits.VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT;
        srcAccessMask = VkAccessFlagBits.VK_ACCESS_NONE_KHR;
        dstStageMask = VkPipelineStageFlagBits.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT | VkPipelineStageFlagBits.VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT;
        dstAccessMask = VkAccessFlagBits.VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT | VkAccessFlagBits.VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
    }

    render_pass_info: VkRenderPassCreateInfo = {
        attachmentCount = attachments.length;
        pAttachments = attachments.data;
        subpassCount = 1;
        pSubpasses = &subpass;
        dependencyCount = 1;
        pDependencies = &dependency;
    }

    result := vkCreateRenderPass(device, &render_pass_info, null, &render_pass);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create render pass %\n", result);
        exit_program(1);
    }
}


// Part 13: https://vulkan-tutorial.com/en/Drawing_a_triangle/Drawing/Framebuffers
swap_chain_framebuffers: Array<VkFramebuffer*>;

create_framebuffers() {
    array_reserve(&swap_chain_framebuffers, swap_chain_image_views.length);

    attachments: Array<VkImageView*>[3];
    // attachments[0] = color_image_view;
    // attachments[1] = depth_image_view;

    framebuffer_info: VkFramebufferCreateInfo = {
        renderPass = render_pass;
        attachmentCount = attachments.length;
        pAttachments = attachments.data;
        width = swap_chain_extent.width;
        height = swap_chain_extent.height;
        layers = 1;
    }

    each image_view, i in swap_chain_image_views {
        attachments = [color_image_view, depth_image_view, image_view]
        // attachments[2] = image_view;

        result := vkCreateFramebuffer(device, &framebuffer_info, null, &swap_chain_framebuffers[i]);
        if result != VkResult.VK_SUCCESS {
            print("Unable to create framebuffer %\n", result);
            exit_program(1);
        }
    }
}


// Part 14: https://vulkan-tutorial.com/en/Drawing_a_triangle/Drawing/Command_buffers
command_pool: VkCommandPool*;
command_buffers: Array<VkCommandBuffer*>;

create_command_pool() {
    graphics_family, _: u32;
    find_queue_families(physical_device, &graphics_family, &_);

    pool_info: VkCommandPoolCreateInfo = {
        queueFamilyIndex = graphics_family;
    }

    result := vkCreateCommandPool(device, &pool_info, null, &command_pool);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create command pool %\n", result);
        exit_program(1);
    }
}

create_command_buffers() {
    array_reserve(&command_buffers, swap_chain_framebuffers.length);

    alloc_info: VkCommandBufferAllocateInfo = {
        commandPool = command_pool;
        level = VkCommandBufferLevel.VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        commandBufferCount = command_buffers.length;
    }

    result := vkAllocateCommandBuffers(device, &alloc_info, command_buffers.data);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create command pool %\n", result);
        exit_program(1);
    }

    begin_info: VkCommandBufferBeginInfo = {
        pInheritanceInfo = null;
    }

    clear_values: Array<VkClearValue>[2];
    clear_values[0].color.float32[0] = 0.0;
    clear_values[0].color.float32[1] = 0.0;
    clear_values[0].color.float32[2] = 0.0;
    clear_values[0].color.float32[3] = 1.0;
    clear_values[1].depthStencil.depth = 1.0;
    clear_values[1].depthStencil.stencil = 0;

    render_pass_info: VkRenderPassBeginInfo = {
        renderPass = render_pass;
        clearValueCount = clear_values.length;
        pClearValues = clear_values.data;
    }
    render_pass_info.renderArea.extent = swap_chain_extent;

    each command_buffer, i in command_buffers {
        result = vkBeginCommandBuffer(command_buffer, &begin_info);
        if result != VkResult.VK_SUCCESS {
            print("Unable to begin recording command buffer %\n", result);
            exit_program(1);
        }

        render_pass_info.framebuffer = swap_chain_framebuffers[i];
        vkCmdBeginRenderPass(command_buffer, &render_pass_info, VkSubpassContents.VK_SUBPASS_CONTENTS_INLINE);

        vkCmdBindPipeline(command_buffer, VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_GRAPHICS, graphics_pipeline);

        vkCmdBindDescriptorSets(command_buffer, VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline_layout, 0, 1, &descriptor_sets[i], 0, null);

        offset: u64;
        vkCmdBindVertexBuffers(command_buffer, 0, 1, &vertex_buffer, &offset);
        vkCmdBindIndexBuffer(command_buffer, index_buffer, 0, VkIndexType.VK_INDEX_TYPE_UINT32);
        vkCmdDrawIndexed(command_buffer, indices.length, 1, 0, 0, 0);

        vkCmdBindDescriptorSets(command_buffer, VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline_layout, 0, 1, &model_descriptor_sets[i], 0, null);

        vkCmdBindVertexBuffers(command_buffer, 0, 1, &model_vertex_buffer, &offset);
        vkCmdBindIndexBuffer(command_buffer, model_index_buffer, 0, VkIndexType.VK_INDEX_TYPE_UINT32);
        vkCmdDrawIndexed(command_buffer, model_indices.length, 1, 0, 0, 0);

        vkCmdEndRenderPass(command_buffer);

        result = vkEndCommandBuffer(command_buffer);
        if result != VkResult.VK_SUCCESS {
            print("Unable to record command buffer %\n", result);
            exit_program(1);
        }
    }
}


// Part 15: https://vulkan-tutorial.com/en/Drawing_a_triangle/Drawing/Rendering_and_presentation
MAX_FRAMES_IN_FLIGHT := 2; #const
image_available_semaphores: Array<VkSemaphore*>[MAX_FRAMES_IN_FLIGHT];
render_finished_semaphores: Array<VkSemaphore*>[MAX_FRAMES_IN_FLIGHT];
in_flight_fences: Array<VkFence*>[MAX_FRAMES_IN_FLIGHT];
images_in_flight: Array<VkFence*>;
current_frame := 0;

create_sync_objects() {
    array_reserve(&images_in_flight, swap_chain_images.length);
    each image in images_in_flight {
        image = null;
    }

    semaphore_info: VkSemaphoreCreateInfo;
    fence_info: VkFenceCreateInfo = {
        flags = cast(u32, VkFenceCreateFlagBits.VK_FENCE_CREATE_SIGNALED_BIT);
    }

    each i in 0..MAX_FRAMES_IN_FLIGHT-1 {
        result := vkCreateSemaphore(device, &semaphore_info, null, &image_available_semaphores[i]);
        if result != VkResult.VK_SUCCESS {
            print("Unable to create semaphore %\n", result);
            exit_program(1);
        }

        result = vkCreateSemaphore(device, &semaphore_info, null, &render_finished_semaphores[i]);
        if result != VkResult.VK_SUCCESS {
            print("Unable to create semaphore %\n", result);
            exit_program(1);
        }

        result = vkCreateFence(device, &fence_info, null, &in_flight_fences[i]);
        if result != VkResult.VK_SUCCESS {
            print("Unable to create fence %\n", result);
            exit_program(1);
        }
    }
}

wait_stages: Array<VkPipelineStageFlagBits> = [VkPipelineStageFlagBits.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT]

draw_frame() {
    vkWaitForFences(device, 1, &in_flight_fences[current_frame], VK_TRUE, 0xFFFFFFFFFFFFFFFF);

    image_index: u32;
    result := vkAcquireNextImageKHR(device, swap_chain, 0xFFFFFFFFFFFFFFFF, image_available_semaphores[current_frame], null, &image_index);

    if result == VkResult.VK_ERROR_OUT_OF_DATE_KHR {
        framebuffer_resized = false;
        recreate_swap_chain();
        return;
    }
    else if result != VkResult.VK_SUCCESS && result != VkResult.VK_SUBOPTIMAL_KHR {
        print("Failed to acquire swap chain image %\n", result);
        exit_program(1);
    }

    if images_in_flight[image_index] {
        vkWaitForFences(device, 1, &images_in_flight[image_index], VK_TRUE, 0xFFFFFFFFFFFFFFFF);
    }

    images_in_flight[image_index] = in_flight_fences[current_frame];

    update_uniform_buffer(image_index);

    submit_info: VkSubmitInfo = {
        waitSemaphoreCount = 1;
        pWaitSemaphores = &image_available_semaphores[current_frame];
        pWaitDstStageMask = wait_stages.data;
        commandBufferCount = 1;
        pCommandBuffers = &command_buffers[image_index];
        signalSemaphoreCount = 1;
        pSignalSemaphores = &render_finished_semaphores[current_frame];
    }

    vkResetFences(device, 1, &in_flight_fences[current_frame]);

    result = vkQueueSubmit(graphics_queue, 1, &submit_info, in_flight_fences[current_frame]);
    if result != VkResult.VK_SUCCESS {
        print("Failed to submit draw command buffer %\n", result);
        exit_program(1);
    }

    present_info: VkPresentInfoKHR = {
        waitSemaphoreCount = 1;
        pWaitSemaphores = &render_finished_semaphores[current_frame];
        swapchainCount = 1;
        pSwapchains = &swap_chain;
        pImageIndices = &image_index;
    }

    result = vkQueuePresentKHR(present_queue, &present_info);
    if result == VkResult.VK_ERROR_OUT_OF_DATE_KHR || result == VkResult.VK_SUBOPTIMAL_KHR || framebuffer_resized {
        framebuffer_resized = false;
        recreate_swap_chain();
    }
    else if result != VkResult.VK_SUCCESS {
        print("Failed to present swap chain image %\n", result);
        exit_program(1);
    }

    current_frame = (current_frame + 1) % MAX_FRAMES_IN_FLIGHT;

    vkQueueWaitIdle(present_queue);
}


// Part 16: https://vulkan-tutorial.com/en/Drawing_a_triangle/Swap_chain_recreation
framebuffer_resized := false;

recreate_swap_chain() {
    vkDeviceWaitIdle(device);

    cleanup_swap_chain();

    create_swap_chain();
    create_image_views();
    create_render_pass();
    create_graphics_pipeline();
    create_color_resources();
    create_depth_resources();
    create_framebuffers();
    create_uniform_buffers();
    create_descriptor_pool();
    create_descriptor_sets(&descriptor_sets, texture_image_view);
    create_descriptor_sets(&model_descriptor_sets, model_texture_image_view);
    create_command_buffers();
}

cleanup_swap_chain() {
    vkDestroyImageView(device, color_image_view, null);
    vkDestroyImage(device, color_image, null);
    vkFreeMemory(device, color_image_memory, null);

    vkDestroyImageView(device, depth_image_view, null);
    vkDestroyImage(device, depth_image, null);
    vkFreeMemory(device, depth_image_memory, null);

    each framebuffer in swap_chain_framebuffers {
        vkDestroyFramebuffer(device, framebuffer, null);
    }

    each image_view in swap_chain_image_views {
        vkDestroyImageView(device, image_view, null);
    }

    vkFreeCommandBuffers(device, command_pool, command_buffers.length, command_buffers.data);

    vkDestroyPipeline(device, graphics_pipeline, null);
    vkDestroyPipelineLayout(device, pipeline_layout, null);
    vkDestroyRenderPass(device, render_pass, null);
    vkDestroySwapchainKHR(device, swap_chain, null);

    each uniform_buffer, i in uniform_buffers {
        vkDestroyBuffer(device, uniform_buffer, null);
        vkFreeMemory(device, uniform_buffers_memory[i], null);
    }

    vkDestroyDescriptorPool(device, descriptor_pool, null);
}


// Part 17: https://vulkan-tutorial.com/en/Vertex_buffers/Vertex_input_description
struct Vector3 {
    x: float;
    y: float;
    z: float;
}

struct Vertex {
    position: Vector3;
    color: Vector3;
    texture_coord: Vector2;
}

VkVertexInputBindingDescription get_binding_description() {
    binding_description: VkVertexInputBindingDescription = {
        binding = 0;
        stride = size_of(Vertex);
        inputRate = VkVertexInputRate.VK_VERTEX_INPUT_RATE_VERTEX;
    }

    return binding_description;
}


// Part 18: https://vulkan-tutorial.com/en/Vertex_buffers/Vertex_buffer_creation
vertex_buffer: VkBuffer*;
vertex_buffer_memory: VkDeviceMemory*;
vertices: Array<Vertex>[8];

setup_vertices() {
    position: Vector3 = { x = -0.5; y = -0.5; }
    color: Vector3 = { x = 1.0; y = 0.0; z = 0.0; }
    texture_coord: Vector2 = { x = 0.0; y = 0.0; }

    vertices[0].position = position;
    vertices[0].color = color;
    vertices[0].texture_coord = texture_coord;

    position.x = 0.5;
    color.x = 0.0;
    color.y = 1.0;
    texture_coord.x = 1.0;

    vertices[1].position = position;
    vertices[1].color = color;
    vertices[1].texture_coord = texture_coord;

    position.y = 0.5;
    color.y = 0.0;
    color.z = 1.0;
    texture_coord.y = 1.0;

    vertices[2].position = position;
    vertices[2].color = color;
    vertices[2].texture_coord = texture_coord;

    position.x = -0.5;
    color.x = 1.0;
    color.y = 1.0;
    texture_coord.x = 0.0;

    vertices[3].position = position;
    vertices[3].color = color;
    vertices[3].texture_coord = texture_coord;

    position.y = -0.5;
    position.z = -0.5;
    color.y = 0.0;
    color.z = 0.0;
    texture_coord.y = 0.0;

    vertices[4].position = position;
    vertices[4].color = color;
    vertices[4].texture_coord = texture_coord;

    position.x = 0.5;
    color.x = 0.0;
    color.y = 1.0;
    texture_coord.x = 1.0;

    vertices[5].position = position;
    vertices[5].color = color;
    vertices[5].texture_coord = texture_coord;

    position.y = 0.5;
    color.y = 0.0;
    color.z = 1.0;
    texture_coord.y = 1.0;

    vertices[6].position = position;
    vertices[6].color = color;
    vertices[6].texture_coord = texture_coord;

    position.x = -0.5;
    color.x = 1.0;
    color.y = 1.0;
    texture_coord.x = 0.0;

    vertices[7].position = position;
    vertices[7].color = color;
    vertices[7].texture_coord = texture_coord;
}

create_vertex_buffer(Array<Vertex> vertices, VkBuffer** vertex_buffer, VkDeviceMemory** vertex_buffer_memory) {
    size := size_of(Vertex) * vertices.length;

    staging_buffer: VkBuffer*;
    staging_buffer_memory: VkDeviceMemory*;
    create_buffer(size, VkBufferUsageFlagBits.VK_BUFFER_USAGE_TRANSFER_SRC_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, &staging_buffer, &staging_buffer_memory);

    data: void*;
    vkMapMemory(device, staging_buffer_memory, 0, size, 0, &data);
    memory_copy(data, vertices.data, size);
    vkUnmapMemory(device, staging_buffer_memory);

    create_buffer(size, VkBufferUsageFlagBits.VK_BUFFER_USAGE_TRANSFER_DST_BIT | VkBufferUsageFlagBits.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, vertex_buffer, vertex_buffer_memory);

    copy_buffer(staging_buffer, *vertex_buffer, size);

    vkDestroyBuffer(device, staging_buffer, null);
    vkFreeMemory(device, staging_buffer_memory, null);
}

u32 find_memory_type(u32 type_filter, VkMemoryPropertyFlagBits properties) {
    memory_properties: VkPhysicalDeviceMemoryProperties;
    vkGetPhysicalDeviceMemoryProperties(physical_device, &memory_properties);

    each i in 0..memory_properties.memoryTypeCount-1 {
        if (type_filter & (1 << i)) > 0 && (memory_properties.memoryTypes[i].propertyFlags & properties) == properties
            return i;
    }

    print("Failed to find a suitable memory type\n");
    exit_program(1);
    return 0;
}


// Part 19: https://vulkan-tutorial.com/Vertex_buffers/Staging_buffer
create_buffer(u64 size, VkBufferUsageFlagBits usage, VkMemoryPropertyFlagBits properties, VkBuffer** buffer, VkDeviceMemory** buffer_memory) {
    buffer_info: VkBufferCreateInfo = {
        size = size;
        usage = usage;
        sharingMode = VkSharingMode.VK_SHARING_MODE_EXCLUSIVE;
    }

    result := vkCreateBuffer(device, &buffer_info, null, buffer);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create buffer %\n", result);
        exit_program(1);
    }

    memory_requirements: VkMemoryRequirements;
    vkGetBufferMemoryRequirements(device, *buffer, &memory_requirements);

    alloc_info: VkMemoryAllocateInfo = {
        allocationSize = memory_requirements.size;
        memoryTypeIndex = find_memory_type(memory_requirements.memoryTypeBits, properties);
    }

    result = vkAllocateMemory(device, &alloc_info, null, buffer_memory);
    if result != VkResult.VK_SUCCESS {
        print("Unable to allocate buffer memory %\n", result);
        exit_program(1);
    }

    vkBindBufferMemory(device, *buffer, *buffer_memory, 0);
}

copy_buffer(VkBuffer* source_buffer, VkBuffer* dest_buffer, u64 size) {
    command_buffer := begin_single_time_commands();

    copy_region: VkBufferCopy = {
        srcOffset = 0;
        dstOffset = 0;
        size = size;
    }

    vkCmdCopyBuffer(command_buffer, source_buffer, dest_buffer, 1, &copy_region);

    end_single_time_commands(command_buffer);
}


// Part 20: https://vulkan-tutorial.com/en/Vertex_buffers/Index_buffer
index_buffer: VkBuffer*;
index_buffer_memory: VkDeviceMemory*;
indices: Array<u32> = [0, 1, 2, 2, 3, 0, 4, 5, 6, 6, 7, 4]

create_index_buffer(Array<u32> indices, VkBuffer** index_buffer, VkDeviceMemory** index_buffer_memory) {
    size := size_of(u32) * indices.length;

    staging_buffer: VkBuffer*;
    staging_buffer_memory: VkDeviceMemory*;
    create_buffer(size, VkBufferUsageFlagBits.VK_BUFFER_USAGE_TRANSFER_SRC_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, &staging_buffer, &staging_buffer_memory);

    data: void*;
    vkMapMemory(device, staging_buffer_memory, 0, size, 0, &data);
    memory_copy(data, indices.data, size);
    vkUnmapMemory(device, staging_buffer_memory);

    create_buffer(size, VkBufferUsageFlagBits.VK_BUFFER_USAGE_TRANSFER_DST_BIT | VkBufferUsageFlagBits.VK_BUFFER_USAGE_INDEX_BUFFER_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, index_buffer, index_buffer_memory);

    copy_buffer(staging_buffer, *index_buffer, size);

    vkDestroyBuffer(device, staging_buffer, null);
    vkFreeMemory(device, staging_buffer_memory, null);
}


// Part 21: https://vulkan-tutorial.com/en/Uniform_buffers/Descriptor_layout_and_buffer
struct Vector4 {
    x: float;
    y: float;
    z: float;
    w: float;
}

struct Matrix4 {
    a: Vector4;
    b: Vector4;
    c: Vector4;
    d: Vector4;
}

struct UniformBufferObject {
    model: Matrix4;
    view: Matrix4;
    projection: Matrix4;
}

descriptor_set_layout: VkDescriptorSetLayout*;

create_descriptor_set_layout() {
    ubo_layout_binding: VkDescriptorSetLayoutBinding = {
        binding = 0;
        descriptorType = VkDescriptorType.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
        descriptorCount = 1;
        stageFlags = VkShaderStageFlagBits.VK_SHADER_STAGE_VERTEX_BIT;
        pImmutableSamplers = null; // Optional
    }

    sampler_layout_binding: VkDescriptorSetLayoutBinding = {
        binding = 1;
        descriptorType = VkDescriptorType.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
        descriptorCount = 1;
        stageFlags = VkShaderStageFlagBits.VK_SHADER_STAGE_FRAGMENT_BIT;
    }

    bindings: Array<VkDescriptorSetLayoutBinding> = [ubo_layout_binding, sampler_layout_binding]

    layout_info: VkDescriptorSetLayoutCreateInfo = {
        bindingCount = bindings.length;
        pBindings = bindings.data;
    }

    result := vkCreateDescriptorSetLayout(device, &layout_info, null, &descriptor_set_layout);
    if result != VkResult.VK_SUCCESS {
        print("Failed to create descriptor set layout %", result);
        exit_program(1);
    }
}

uniform_buffers: Array<VkBuffer*>;
uniform_buffers_memory: Array<VkDeviceMemory*>;

create_uniform_buffers() {
    size := size_of(UniformBufferObject);

    array_reserve(&uniform_buffers, swap_chain_images.length);
    array_reserve(&uniform_buffers_memory, swap_chain_images.length);

    each uniform_buffer, i in uniform_buffers {
        create_buffer(size, VkBufferUsageFlagBits.VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, &uniform_buffer, &uniform_buffers_memory[i]);
    }
}

start: u64;

update_uniform_buffer(u32 current_image) {
    now := get_performance_counter();
    time_diff := cast(float, now - start) / 100000000;

    ubo: UniformBufferObject = {
        model = mat4_rotate_z(radians(time_diff));
        view = look_at(vec3(2.0, 2.0, 2.0), vec3(), vec3(z = 1.0));
        projection = perspective(radians(45.0), swap_chain_extent.width / cast(float, swap_chain_extent.height), 0.1, 10.0);
    }

    size := size_of(ubo);

    data: void*;
    vkMapMemory(device, uniform_buffers_memory[current_image], 0, size, 0, &data);
    memory_copy(data, &ubo, size);
    vkUnmapMemory(device, uniform_buffers_memory[current_image]);
}

pi := 3.14159265359; #const

float radians(float degrees) {
    return degrees * pi / 180;
}

Matrix4 mat4_ident() {
    matrix: Matrix4 = {
        a = { x = 1.0; }
        b = { y = 1.0; }
        c = { z = 1.0; }
        d = { w = 1.0; }
    }
    return matrix;
}

Vector4 vec4(float x = 0.0, float y = 0.0, float z = 0.0, float w = 0.0) {
    vector: Vector4 = { x = x; y = y; z = z; w = w; }
    return vector;
}

// rotate, look_at, and perspective borrowed from https://github.com/g-truc/glm
Matrix4 mat4_rotate_z(float angle) {
    sin := sine(angle);
    cos := cosine(angle);

    matrix := mat4_ident();
    matrix.a.x = cos;
    matrix.a.y = sin;
    matrix.b.x = -sin;
    matrix.b.y = cos;

    return matrix;
}

Matrix4 look_at(Vector3 eye, Vector3 center, Vector3 up) {
    sub := sub(center, eye);
    f := normalize(sub);
    cross := cross(f, up);
    s := normalize(cross);
    u := cross(s, f);

    result := mat4_ident();

    result.a.x = s.x;
    result.b.x = s.y;
    result.c.x = s.z;

    result.a.y = u.x;
    result.b.y = u.y;
    result.c.y = u.z;

    result.a.z = -f.x;
    result.b.z = -f.y;
    result.c.z = -f.z;

    result.d.x = -dot(s, eye);
    result.d.y = -dot(u, eye);
    result.d.z = dot(f, eye);

    return result;
}

Matrix4 perspective(float fovy, float aspect, float z_near, float z_far) {
    tan_half_fovy := tangent(fovy / 2.0);

    result: Matrix4;

    result.a.x = 1 / (aspect * tan_half_fovy);
    result.b.y = -1 / tan_half_fovy;
    result.c.z = -z_far / (z_far - z_near);
    result.c.w = -1.0;
    result.d.z = - (z_far * z_near) / (z_far - z_near);

    return result;
}

Vector3 normalize(Vector3 vec) {
    magnitude := square_root(vec.x * vec.x + vec.y * vec.y + vec.z * vec.z);

    result: Vector3 = {
        x = vec.x / magnitude;
        y = vec.y / magnitude;
        z = vec.z / magnitude;
    }
    return result;
}

Vector3 cross(Vector3 a, Vector3 b) {
    result: Vector3 = {
        x = a.y * b.z - a.z * b.y;
        y = a.z * b.x - a.x * b.z;
        z = a.x * b.y - a.y * b.x;
    }
    return result;
}

float dot(Vector3 a, Vector3 b) {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

Vector3 sub(Vector3 a, Vector3 b) {
    result: Vector3 = {
        x = a.x - b.x;
        y = a.y - b.y;
        z = a.z - b.z;
    }
    return result;
}

Vector3 vec3(float x = 0.0, float y = 0.0, float z = 0.0) {
    vector: Vector3 = { x = x; y = y; z = z; }
    return vector;
}


// Part 22: https://vulkan-tutorial.com/en/Uniform_buffers/Descriptor_pool_and_sets
descriptor_pool: VkDescriptorPool*;
descriptor_sets: Array<VkDescriptorSet*>;

create_descriptor_pool() {
    pool_sizes: Array<VkDescriptorPoolSize>[2];
    descriptor_length := swap_chain_images.length * 2;

    pool_sizes[0].type = VkDescriptorType.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    pool_sizes[0].descriptorCount = descriptor_length;
    pool_sizes[1].type = VkDescriptorType.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    pool_sizes[1].descriptorCount = descriptor_length;

    pool_info: VkDescriptorPoolCreateInfo = {
        poolSizeCount = pool_sizes.length;
        pPoolSizes = pool_sizes.data;
        maxSets = descriptor_length;
    }

    result := vkCreateDescriptorPool(device, &pool_info, null, &descriptor_pool);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create descriptor pool %\n", result);
        exit_program(1);
    }
}

create_descriptor_sets(Array<VkDescriptorSet*>* descriptor_sets, VkImageView* texture_image_view) {
    layouts: Array<VkDescriptorSetLayout*>[swap_chain_images.length];
    each layout in layouts {
        layout = descriptor_set_layout;
    }

    alloc_info: VkDescriptorSetAllocateInfo = {
        descriptorPool = descriptor_pool;
        descriptorSetCount = swap_chain_images.length;
        pSetLayouts = layouts.data;
    }

    array_reserve(descriptor_sets, swap_chain_images.length);

    result := vkAllocateDescriptorSets(device, &alloc_info, descriptor_sets.data);
    if result != VkResult.VK_SUCCESS {
        print("Unable to allocate descriptor sets %\n", result);
        exit_program(1);
    }

    buffer_info: VkDescriptorBufferInfo = {
        offset = 0;
        range = size_of(UniformBufferObject);
    }

    image_info: VkDescriptorImageInfo = {
        imageLayout = VkImageLayout.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        imageView = texture_image_view;
        sampler = texture_sampler;
    }

    descriptor_write: VkWriteDescriptorSet = {
        dstBinding = 0;
        dstArrayElement = 0;
        descriptorType = VkDescriptorType.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
        descriptorCount = 1;
    }
    descriptor_writes: Array<VkWriteDescriptorSet> = [descriptor_write, descriptor_write]

    each uniform_buffer, i in uniform_buffers {
        buffer_info.buffer = uniform_buffer;

        descriptor_writes[0].dstSet = descriptor_sets.data[i];
        descriptor_writes[0].pBufferInfo = &buffer_info;
        descriptor_writes[1].dstSet = descriptor_sets.data[i];
        descriptor_writes[1].dstBinding = 1;
        descriptor_writes[1].descriptorType = VkDescriptorType.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
        descriptor_writes[1].pImageInfo = &image_info;

        vkUpdateDescriptorSets(device, descriptor_writes.length, descriptor_writes.data, 0, null);
    }
}


// Part 23: https://vulkan-tutorial.com/Texture_mapping/Images
texture_image: VkImage*;
texture_image_memory: VkDeviceMemory*;

#library stb_image "lib/stb_image"

u8* stbi_load(string filename, int* x, int* y, int* comp, int req_comp) #extern stb_image
stbi_image_free(void* image) #extern stb_image

int create_texture_image(string file, VkImage** texture_image, VkDeviceMemory** texture_image_memory) {
    width, height, max, channels: int;
    pixels := stbi_load(file, &width, &height, &channels, 4);

    if pixels == null {
        print("Failed to load texture image\n");
        exit_program(1);
    }

    image_size := width * height * 4;

    if width > height max = width;
    else max = height;

    mip_levels: u32 = cast(u32, floor(log_2(cast(float64, max)))) + 1;


    staging_buffer: VkBuffer*;
    staging_buffer_memory: VkDeviceMemory*;

    create_buffer(image_size, VkBufferUsageFlagBits.VK_BUFFER_USAGE_TRANSFER_SRC_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, &staging_buffer, &staging_buffer_memory);

    data: void*;
    vkMapMemory(device, staging_buffer_memory, 0, image_size, 0, &data);
    memory_copy(data, pixels, image_size);
    vkUnmapMemory(device, staging_buffer_memory);

    stbi_image_free(pixels);

    create_image(width, height, VkFormat.VK_FORMAT_R8G8B8A8_SRGB, VkImageTiling.VK_IMAGE_TILING_OPTIMAL, VkImageUsageFlagBits.VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VkImageUsageFlagBits.VK_IMAGE_USAGE_TRANSFER_DST_BIT | VkImageUsageFlagBits.VK_IMAGE_USAGE_SAMPLED_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT, texture_image, texture_image_memory, mip_levels);

    transition_image_layout(*texture_image, VkFormat.VK_FORMAT_R8G8B8A8_SRGB, VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED, VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, mip_levels);

    copy_buffer_to_image(staging_buffer, *texture_image, width, height);

    vkDestroyBuffer(device, staging_buffer, null);
    vkFreeMemory(device, staging_buffer_memory, null);

    generate_mipmaps(*texture_image, VkFormat.VK_FORMAT_R8G8B8A8_SRGB, width, height, mip_levels);

    return mip_levels;
}

create_image(u32 width, u32 height, VkFormat format, VkImageTiling tiling, VkImageUsageFlagBits usage, VkMemoryPropertyFlagBits properties, VkImage** image, VkDeviceMemory** image_memory, u32 mip_levels = 1, VkSampleCountFlagBits samples = VkSampleCountFlagBits.VK_SAMPLE_COUNT_1_BIT) {
    image_info: VkImageCreateInfo = {
        flags = 0; // Optional
        imageType = VkImageType.VK_IMAGE_TYPE_2D;
        format = format;
        mipLevels = mip_levels;
        arrayLayers = 1;
        samples = samples;
        tiling = tiling;
        usage = usage;
        sharingMode = VkSharingMode.VK_SHARING_MODE_EXCLUSIVE;
        initialLayout = VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED;
        extent = {
            width = width;
            height = height;
            depth = 1;
        }
    }

    result := vkCreateImage(device, &image_info, null, image);
    if result != VkResult.VK_SUCCESS {
        print("Failed to create image %\n", result);
        exit_program(1);
    }

    memory_requirements: VkMemoryRequirements;
    vkGetImageMemoryRequirements(device, *image, &memory_requirements);

    alloc_info: VkMemoryAllocateInfo = {
        allocationSize = memory_requirements.size;
        memoryTypeIndex = find_memory_type(memory_requirements.memoryTypeBits, properties);
    }

    result = vkAllocateMemory(device, &alloc_info, null, image_memory);
    if result != VkResult.VK_SUCCESS {
        print("Failed to allocate image memory %\n", result);
        exit_program(1);
    }

    vkBindImageMemory(device, *image, *image_memory, 0);
}

VkCommandBuffer* begin_single_time_commands() {
    alloc_info: VkCommandBufferAllocateInfo = {
        level = VkCommandBufferLevel.VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        commandPool = command_pool;
        commandBufferCount = 1;
    }

    command_buffer: VkCommandBuffer*;
    vkAllocateCommandBuffers(device, &alloc_info, &command_buffer);

    begin_info: VkCommandBufferBeginInfo = {
        flags = cast(u32, VkCommandBufferUsageFlagBits.VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT);
    }

    vkBeginCommandBuffer(command_buffer, &begin_info);

    return command_buffer;
}

end_single_time_commands(VkCommandBuffer* command_buffer) {
    vkEndCommandBuffer(command_buffer);

    submit_info: VkSubmitInfo = {
        commandBufferCount = 1;
        pCommandBuffers = &command_buffer;
    }

    vkQueueSubmit(graphics_queue, 1, &submit_info, null);
    vkQueueWaitIdle(graphics_queue);

    vkFreeCommandBuffers(device, command_pool, 1, &command_buffer);
}

transition_image_layout(VkImage* image, VkFormat format, VkImageLayout old_layout, VkImageLayout new_layout, u32 mip_levels) {
    command_buffer := begin_single_time_commands();

    barrier: VkImageMemoryBarrier = {
        oldLayout = old_layout;
        newLayout = new_layout;
        srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        image = image;
        subresourceRange = {
            baseMipLevel = 0;
            levelCount = mip_levels;
            baseArrayLayer = 0;
            layerCount = 1;
        }
    }

    if new_layout == VkImageLayout.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL {
        barrier.subresourceRange.aspectMask = VkImageAspectFlagBits.VK_IMAGE_ASPECT_DEPTH_BIT;

        if has_stencil_component(format) {
            barrier.subresourceRange.aspectMask |= VkImageAspectFlagBits.VK_IMAGE_ASPECT_STENCIL_BIT;
        }
    }
    else {
        barrier.subresourceRange.aspectMask = VkImageAspectFlagBits.VK_IMAGE_ASPECT_COLOR_BIT;
    }

    source_stage, destination_stage: VkPipelineStageFlagBits;

    if old_layout == VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED && new_layout == VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL {
        barrier.dstAccessMask = VkAccessFlagBits.VK_ACCESS_TRANSFER_WRITE_BIT;
        source_stage = VkPipelineStageFlagBits.VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
        destination_stage = VkPipelineStageFlagBits.VK_PIPELINE_STAGE_TRANSFER_BIT;
    }
    else if old_layout == VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL && new_layout == VkImageLayout.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL {
        barrier.srcAccessMask = VkAccessFlagBits.VK_ACCESS_TRANSFER_WRITE_BIT;
        barrier.dstAccessMask = VkAccessFlagBits.VK_ACCESS_SHADER_READ_BIT;
        source_stage = VkPipelineStageFlagBits.VK_PIPELINE_STAGE_TRANSFER_BIT;
        destination_stage = VkPipelineStageFlagBits.VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
    }
    else if old_layout == VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED && new_layout == VkImageLayout.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL {
        barrier.dstAccessMask = VkAccessFlagBits.VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VkAccessFlagBits.VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
        source_stage = VkPipelineStageFlagBits.VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
        destination_stage = VkPipelineStageFlagBits.VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT;
    }
    else {
        print("Unsupported layout transition\n");
        exit_program(1);
    }

    vkCmdPipelineBarrier(command_buffer, source_stage, destination_stage, 0, 0, null, 0, null, 1, &barrier);

    end_single_time_commands(command_buffer);
}

copy_buffer_to_image(VkBuffer* buffer, VkImage* image, u32 width, u32 height) {
    command_buffer := begin_single_time_commands();

    region: VkBufferImageCopy = {
        bufferOffset = 0;
        bufferRowLength = 0;
        bufferImageHeight = 0;
        imageSubresource = {
            aspectMask = VkImageAspectFlagBits.VK_IMAGE_ASPECT_COLOR_BIT;
            mipLevel = 0;
            baseArrayLayer = 0;
            layerCount = 1;
        }
        imageExtent = {
            width = width;
            height = height;
            depth = 1;
        }
    }

    vkCmdCopyBufferToImage(command_buffer, buffer, image, VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);

    end_single_time_commands(command_buffer);
}


// Part 24: https://vulkan-tutorial.com/en/Texture_mapping/Image_view_and_sampler
texture_image_view: VkImageView*;
texture_sampler: VkSampler*;

create_texture_image_view(VkImage* texture_image, VkImageView** texture_image_view, u32 mip_levels) {
    view_info: VkImageViewCreateInfo = {
        image = texture_image;
        viewType = VkImageViewType.VK_IMAGE_VIEW_TYPE_2D;
        format = VkFormat.VK_FORMAT_R8G8B8A8_SRGB;
        subresourceRange = {
            aspectMask = VkImageAspectFlagBits.VK_IMAGE_ASPECT_COLOR_BIT;
            baseMipLevel = 0;
            levelCount = mip_levels;
            baseArrayLayer = 0;
            layerCount = 1;
        }
    }

    result := vkCreateImageView(device, &view_info, null, texture_image_view);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create image view %\n", result);
        exit_program(1);
    }
}

create_texture_sampler() {
    properties: VkPhysicalDeviceProperties;
    vkGetPhysicalDeviceProperties(physical_device, &properties);

    sampler_info: VkSamplerCreateInfo = {
        magFilter = VkFilter.VK_FILTER_LINEAR;
        minFilter = VkFilter.VK_FILTER_LINEAR;
        addressModeU = VkSamplerAddressMode.VK_SAMPLER_ADDRESS_MODE_REPEAT;
        addressModeV = VkSamplerAddressMode.VK_SAMPLER_ADDRESS_MODE_REPEAT;
        addressModeW = VkSamplerAddressMode.VK_SAMPLER_ADDRESS_MODE_REPEAT;
        anisotropyEnable = VK_TRUE;
        maxAnisotropy = properties.limits.maxSamplerAnisotropy;
        borderColor = VkBorderColor.VK_BORDER_COLOR_INT_OPAQUE_BLACK;
        unnormalizedCoordinates = VK_FALSE;
        compareEnable = VK_FALSE;
        compareOp = VkCompareOp.VK_COMPARE_OP_ALWAYS;
        mipmapMode = VkSamplerMipmapMode.VK_SAMPLER_MIPMAP_MODE_LINEAR;
        mipLodBias = 0.0;
        minLod = 0.0;
        maxLod = 10.0;
    }

    result := vkCreateSampler(device, &sampler_info, null, &texture_sampler);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create image sampler %\n", result);
        exit_program(1);
    }
}


// Part 25: https://vulkan-tutorial.com/en/Texture_mapping/Combined_image_sampler
struct Vector2 {
    x: float;
    y: float;
}


// Part 26: https://vulkan-tutorial.com/en/Depth_buffering
depth_image: VkImage*;
depth_image_memory: VkDeviceMemory*;
depth_image_view: VkImageView*;

create_depth_resources() {
    depth_format := find_depth_format();

    create_image(swap_chain_extent.width, swap_chain_extent.height, depth_format, VkImageTiling.VK_IMAGE_TILING_OPTIMAL, VkImageUsageFlagBits.VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT, &depth_image, &depth_image_memory, samples = msaa_samples);

    view_create_info: VkImageViewCreateInfo = {
        image = depth_image;
        viewType = VkImageViewType.VK_IMAGE_VIEW_TYPE_2D;
        format = depth_format;
        subresourceRange = {
            aspectMask = VkImageAspectFlagBits.VK_IMAGE_ASPECT_DEPTH_BIT;
            baseMipLevel = 0;
            levelCount = 1;
            baseArrayLayer = 0;
            layerCount = 1;
        }
    }

    result := vkCreateImageView(device, &view_create_info, null, &depth_image_view);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create image view %\n", result);
        exit_program(1);
    }

    transition_image_layout(depth_image, depth_format, VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED, VkImageLayout.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL, 1);
}

VkFormat find_supported_format(Array<VkFormat> candidates, VkImageTiling tiling, VkFormatFeatureFlagBits features) {
    props: VkFormatProperties;

    each format in candidates {
        vkGetPhysicalDeviceFormatProperties(physical_device, format, &props);
        if tiling == VkImageTiling.VK_IMAGE_TILING_LINEAR && (props.linearTilingFeatures & features) == features
            return format;
        else if tiling == VkImageTiling.VK_IMAGE_TILING_OPTIMAL && (props.optimalTilingFeatures & features) == features
            return format;
    }

    print("Failed to find supported format\n");
    exit_program(1);
    return VkFormat.VK_FORMAT_UNDEFINED;
}

depth_formats: Array<VkFormat> = [VkFormat.VK_FORMAT_D32_SFLOAT, VkFormat.VK_FORMAT_D32_SFLOAT_S8_UINT, VkFormat.VK_FORMAT_D24_UNORM_S8_UINT]

VkFormat find_depth_format() {
    return find_supported_format(depth_formats, VkImageTiling.VK_IMAGE_TILING_OPTIMAL, VkFormatFeatureFlagBits.VK_FORMAT_FEATURE_DEPTH_STENCIL_ATTACHMENT_BIT);
}

bool has_stencil_component(VkFormat format) {
    return format == VkFormat.VK_FORMAT_D32_SFLOAT_S8_UINT || format == VkFormat.VK_FORMAT_D24_UNORM_S8_UINT;
}


// Part 27: https://vulkan-tutorial.com/Loading_models
model_vertex_buffer: VkBuffer*;
model_vertex_buffer_memory: VkDeviceMemory*;
model_index_buffer: VkBuffer*;
model_index_buffer_memory: VkDeviceMemory*;
model_texture_image: VkImage*;
model_texture_image_memory: VkDeviceMemory*;
model_texture_image_view: VkImageView*;
model_descriptor_sets: Array<VkDescriptorSet*>;

model_vertices: Array<Vertex>;
model_indices: Array<u32>;

load_model() {
    _, model_file := read_file("test/tests/vulkan/models/viking_room.obj");

    // Get the indices and line count of each component
    vt, vertex_count := find_next(model_file, "vt");
    vn, texture_count := find_next(model_file, "vn", vt);
    f, vn_count := find_next(model_file, "f", vn); // Skipping normals for now
    face_count := find_remaining_lines(model_file, f);

    model_vertex_array: Array<Vector3>[vertex_count];
    model_texture_coordinates: Array<Vector2>[texture_count];

    // Load vertices
    i := 0;
    vertex_index := 0;
    while i < vt {
        // Move over 'v '
        i += 2;
        x := parse_float(model_file.data + i);
        while model_file[++i] != ' ' {}
        i++;

        y := parse_float(model_file.data + i);
        while model_file[++i] != ' ' {}
        i++;

        z := parse_float(model_file.data + i) + 0.5;
        while model_file[++i] != '\n' {}
        i++;

        vertex: Vector3 = { x = x; y = y; z = z; }
        model_vertex_array[vertex_index++] = vertex;
    }

    // Load texture indices
    coord_index := 0;
    while i < vn {
        // Move over 'vt '
        i += 3;
        x := parse_float(model_file.data + i);
        while model_file[++i] != ' ' {}
        i++;

        y := 1.0 - parse_float(model_file.data + i);
        while model_file[++i] != '\n' {}
        i++;

        texture_coordinate: Vector2 = { x = x; y = y; }
        model_texture_coordinates[coord_index++] = texture_coordinate;
    }
    i = f;

    // Load indices
    array_reserve(&model_vertices, face_count * 3);
    array_reserve(&model_indices, face_count * 3);

    color: Vector3 = { x = 1.0; y = 1.0; z = 1.0; }
    vertex: Vertex = { color = color; }

    index := 0;
    while i < model_file.length {
        // Move over 'f '
        i += 2;
        v, t, n: int;

        i += parse_obj_line(model_file.data + i, ' ', &v, &t, &n);
        vertex.position = model_vertex_array[v-1];
        vertex.texture_coord = model_texture_coordinates[t-1];
        model_vertices[index] = vertex;
        model_indices[index] = index;
        index++;

        i += parse_obj_line(model_file.data + i, ' ', &v, &t, &n);
        vertex.position = model_vertex_array[v-1];
        vertex.texture_coord = model_texture_coordinates[t-1];
        model_vertices[index] = vertex;
        model_indices[index] = index;
        index++;

        i += parse_obj_line(model_file.data + i, '\n', &v, &t, &n);
        vertex.position = model_vertex_array[v-1];
        vertex.texture_coord = model_texture_coordinates[t-1];
        model_vertices[index] = vertex;
        model_indices[index] = index;
        index++;
    }

    default_free(model_file.data);

    create_vertex_buffer(model_vertices, &model_vertex_buffer, &model_vertex_buffer_memory);
    create_index_buffer(model_indices, &model_index_buffer, &model_index_buffer_memory);
}

s64, int find_next(string value, string match, s64 index = 0) {
    lines := 0;

    while index < value.length {
        char := value[index];
        if char == '\n' {
            lines++;
        }
        else if char == match[0] {
            matches := true;
            each i in 1..match.length-1 {
                if value[index + i] != match[i] {
                    matches = false;
                    break;
                }
            }
            if matches return index, lines;
        }
        index++;
    }

    return index, lines;
}

int find_remaining_lines(string value, s64 index = 0) {
    lines := 0;

    while index < value.length {
        char := value[index++];
        if char == '\n' {
            lines++;
        }
    }

    return lines;
}

float parse_float(u8* str) {
    whole := 0;
    decimal := 0.0;
    i := 0;

    negative := false;
    digit := str[i++];
    if digit == '-' {
        negative = true;
        digit = str[i++];
    }

    parsing_decimal := false;
    decimal_factor := 0.1;
    while digit == '.' || (digit >= '0' && digit <= '9') {
        if digit == '.' {
            parsing_decimal = true;
        }
        else if parsing_decimal {
            decimal += decimal_factor * (digit - '0');
            decimal_factor /= 10.0;
        }
        else {
            whole *= 10;
            whole += digit - '0';
        }
        digit = str[i++];
    }

    value := whole + decimal;
    if negative value *= -1.0;

    return value;
}

int parse_obj_line(u8* str, u8 end_char, int* x, int* y, int* z) {
    value := 0;
    i := 0;

    digit := str[i++];
    while digit != '/' {
        value *= 10;
        value += digit - '0';
        digit = str[i++];
    }
    *x = value;

    value = 0;
    digit = str[i++];
    while digit != '/' {
        value *= 10;
        value += digit - '0';
        digit = str[i++];
    }
    *y = value;

    value = 0;
    digit = str[i++];
    while digit != end_char {
        value *= 10;
        value += digit - '0';
        digit = str[i++];
    }
    *z = value;

    return i;
}


// Part 28: https://vulkan-tutorial.com/Generating_Mipmaps
generate_mipmaps(VkImage* image, VkFormat image_format, int width, int height, int mip_levels) {
    // Check if image format supports linear blitting
    format_properties: VkFormatProperties;
    vkGetPhysicalDeviceFormatProperties(physical_device, image_format, &format_properties);

    if (format_properties.optimalTilingFeatures & VkFormatFeatureFlagBits.VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT) != VkFormatFeatureFlagBits.VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT {
        print("Texture image format does not support linear blitting\n");
        exit_program(1);
    }

    command_buffer := begin_single_time_commands();

    barrier: VkImageMemoryBarrier = {
        srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        image = image;
        subresourceRange = {
            aspectMask = VkImageAspectFlagBits.VK_IMAGE_ASPECT_COLOR_BIT;
            levelCount = 1;
            baseArrayLayer = 0;
            layerCount = 1;
        }
    }

    blit: VkImageBlit = {
        srcSubresource = {
            aspectMask = VkImageAspectFlagBits.VK_IMAGE_ASPECT_COLOR_BIT;
            baseArrayLayer = 0;
            layerCount = 1;
        }
        // srcOffsets = [1, 2]
        dstSubresource = {
            aspectMask = VkImageAspectFlagBits.VK_IMAGE_ASPECT_COLOR_BIT;
            baseArrayLayer = 0;
            layerCount = 1;
        }
        // dstOffsets = []
    }
    blit.srcOffsets[0].x = 0;
    blit.srcOffsets[0].y = 0;
    blit.srcOffsets[0].z = 0;
    blit.srcOffsets[1].z = 1;
    blit.dstOffsets[0].x = 0;
    blit.dstOffsets[0].y = 0;
    blit.dstOffsets[0].z = 0;
    blit.dstOffsets[1].z = 1;

    each i in 1..mip_levels-1 {
        barrier = {
            srcAccessMask = VkAccessFlagBits.VK_ACCESS_TRANSFER_WRITE_BIT;
            dstAccessMask = VkAccessFlagBits.VK_ACCESS_TRANSFER_READ_BIT;
            oldLayout = VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            newLayout = VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
            subresourceRange = { baseMipLevel = i - 1; }
        }
        blit = {
            srcSubresource = { mipLevel = i - 1; }
            dstSubresource = { mipLevel = i; }
        }
        blit.srcOffsets[1].x = width;
        blit.srcOffsets[1].y = height;

        if width > 1 {
            blit.dstOffsets[1].x = width / 2;
            blit.dstOffsets[1].y = height / 2;
            width /= 2;
            height /= 2;
        }
        else {
            blit.dstOffsets[1].x = 1;
            blit.dstOffsets[1].y = 1;
        }

        vkCmdPipelineBarrier(command_buffer, VkPipelineStageFlagBits.VK_PIPELINE_STAGE_TRANSFER_BIT, VkPipelineStageFlagBits.VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, null, 0, null, 1, &barrier);

        vkCmdBlitImage(command_buffer, image, VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, image, VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &blit, VkFilter.VK_FILTER_LINEAR);

        barrier = {
            srcAccessMask = VkAccessFlagBits.VK_ACCESS_TRANSFER_READ_BIT;
            dstAccessMask = VkAccessFlagBits.VK_ACCESS_SHADER_READ_BIT;
            oldLayout = VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
            newLayout = VkImageLayout.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        }

        vkCmdPipelineBarrier(command_buffer, VkPipelineStageFlagBits.VK_PIPELINE_STAGE_TRANSFER_BIT, VkPipelineStageFlagBits.VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0, null, 0, null, 1, &barrier);
    }

    barrier = {
        srcAccessMask = VkAccessFlagBits.VK_ACCESS_TRANSFER_WRITE_BIT;
        dstAccessMask = VkAccessFlagBits.VK_ACCESS_SHADER_READ_BIT;
        oldLayout = VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        newLayout = VkImageLayout.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        subresourceRange = { baseMipLevel = mip_levels - 1; }
    }

    vkCmdPipelineBarrier(command_buffer, VkPipelineStageFlagBits.VK_PIPELINE_STAGE_TRANSFER_BIT, VkPipelineStageFlagBits.VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0, null, 0, null, 1, &barrier);

    end_single_time_commands(command_buffer);
}


// Part 29: https://vulkan-tutorial.com/en/Multisampling
color_image: VkImage*;
color_image_memory: VkDeviceMemory*;
color_image_view: VkImageView*;

msaa_samples := VkSampleCountFlagBits.VK_SAMPLE_COUNT_1_BIT;

VkSampleCountFlagBits get_max_usable_sample_count() {
    properties: VkPhysicalDeviceProperties;
    vkGetPhysicalDeviceProperties(physical_device, &properties);

    counts := properties.limits.framebufferColorSampleCounts & properties.limits.framebufferDepthSampleCounts;

    if counts & VkSampleCountFlagBits.VK_SAMPLE_COUNT_64_BIT return VkSampleCountFlagBits.VK_SAMPLE_COUNT_64_BIT;
    if counts & VkSampleCountFlagBits.VK_SAMPLE_COUNT_32_BIT return VkSampleCountFlagBits.VK_SAMPLE_COUNT_32_BIT;
    if counts & VkSampleCountFlagBits.VK_SAMPLE_COUNT_16_BIT return VkSampleCountFlagBits.VK_SAMPLE_COUNT_16_BIT;
    if counts & VkSampleCountFlagBits.VK_SAMPLE_COUNT_8_BIT return VkSampleCountFlagBits.VK_SAMPLE_COUNT_8_BIT;
    if counts & VkSampleCountFlagBits.VK_SAMPLE_COUNT_4_BIT return VkSampleCountFlagBits.VK_SAMPLE_COUNT_4_BIT;
    if counts & VkSampleCountFlagBits.VK_SAMPLE_COUNT_2_BIT return VkSampleCountFlagBits.VK_SAMPLE_COUNT_2_BIT;

    return VkSampleCountFlagBits.VK_SAMPLE_COUNT_1_BIT;
}

create_color_resources() {
    color_format := swap_chain_format;

    create_image(swap_chain_extent.width, swap_chain_extent.height, color_format, VkImageTiling.VK_IMAGE_TILING_OPTIMAL, VkImageUsageFlagBits.VK_IMAGE_USAGE_TRANSIENT_ATTACHMENT_BIT | VkImageUsageFlagBits.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT, &color_image, &color_image_memory, samples = msaa_samples);

    view_create_info: VkImageViewCreateInfo = {
        image = color_image;
        viewType = VkImageViewType.VK_IMAGE_VIEW_TYPE_2D;
        format = color_format;
        subresourceRange = {
            aspectMask = VkImageAspectFlagBits.VK_IMAGE_ASPECT_COLOR_BIT;
            baseMipLevel = 0;
            levelCount = 1;
            baseArrayLayer = 0;
            layerCount = 1;
        }
    }

    result := vkCreateImageView(device, &view_create_info, null, &color_image_view);
    if result != VkResult.VK_SUCCESS {
        print("Unable to create image view %\n", result);
        exit_program(1);
    }
}


#run {
    main();
    set_output_type_table(OutputTypeTableConfiguration.Used);

    if os != OS.Windows {
        set_linker(LinkerType.Dynamic);
    }
}
