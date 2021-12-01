#import vulkan
#import file

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

    create_framebuffers();

    create_command_pool();

    setup_vertices();

    create_vertex_buffer();

    create_index_buffer();

    create_uniform_buffers();

    create_command_buffers();

    create_sync_objects();
}


// Part 1: https://vulkan-tutorial.com/Drawing_a_triangle/Setup/Instance
instance: VkInstance*;

create_instance() {
    if enable_validation_layers && !check_validation_layer_support() {
        printf("Validation layers requested, but not available\n");
        exit(1);
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

    printf("Creating vulkan instance\n");
    result := vkCreateInstance(&instance_create_info, null, &instance);
    if result != VkResult.VK_SUCCESS {
        printf("Unable to create vulkan instance %d\n", result);
        exit(1);
    }
}

Array<u8*> get_required_extensions() {
    extension_count: u32;
    result := vkEnumerateInstanceExtensionProperties(null, &extension_count, null);
    if result != VkResult.VK_SUCCESS {
        printf("Unable to get vulkan extensions\n");
        exit(1);
    }

    extensions: Array<VkExtensionProperties>[extension_count];
    vkEnumerateInstanceExtensionProperties(null, &extension_count, extensions.data);

    extension_names: Array<u8*>;
    each extension, i in extensions {
        name := convert_c_string(&extension.extensionName);
        printf("Extension - %s\n", name);

        #if os == OS.Linux {
            if name == VK_KHR_SURFACE_EXTENSION_NAME {
                array_insert(&extension_names, VK_KHR_SURFACE_EXTENSION_NAME.data);
            }
            else if name == VK_KHR_XLIB_SURFACE_EXTENSION_NAME {
                array_insert(&extension_names, VK_KHR_XLIB_SURFACE_EXTENSION_NAME.data);
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

    vkDestroyDescriptorSetLayout(device, descriptor_set_layout, null);
    vkDestroyBuffer(device, index_buffer, null);
    vkFreeMemory(device, index_buffer_memory, null);
    vkDestroyBuffer(device, vertex_buffer, null);
    vkFreeMemory(device, vertex_buffer_memory, null);
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
            printf("Failed to set up debug messenger %d\n", result);
            exit(1);
        }
    }
    else {
        printf("Failed to set up debug messenger\n");
        exit(1);
    }
}

u32 debug_callback(VkDebugUtilsMessageSeverityFlagBitsEXT severity, VkDebugUtilsMessageTypeFlagBitsEXT type, VkDebugUtilsMessengerCallbackDataEXT* callback_data, void* user_data) {
    if severity == VkDebugUtilsMessageSeverityFlagBitsEXT.VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT {
        printf("Warning - %s\n", callback_data.pMessage);
    }
    else if severity == VkDebugUtilsMessageSeverityFlagBitsEXT.VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT {
        printf("Error - %s\n", callback_data.pMessage);
    }

    return VK_FALSE;
}


// Part 3: https://vulkan-tutorial.com/Drawing_a_triangle/Setup/Physical_devices_and_queue_families
physical_device: VkPhysicalDevice*;

pick_physical_device() {
    device_count: u32;
    vkEnumeratePhysicalDevices(instance, &device_count, null);

    if device_count == 0 {
        printf("Failed to find GPUs with Vulkan support\n");
        exit(1);
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
        printf("Failed to find a suitable GPU\n");
        exit(1);
    }
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

    printf("Device - %s, Score = %d\n", properties.deviceName, score);

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
    features: VkPhysicalDeviceFeatures;
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
        printf("Unable to create vulkan device %d\n", result);
        exit(1);
    }

    vkGetDeviceQueue(device, graphics_family, 0, &graphics_queue);
    vkGetDeviceQueue(device, present_family, 0, &present_queue);
}


// Part 6: https://vulkan-tutorial.com/en/Drawing_a_triangle/Presentation/Window_surface
surface: VkSurfaceKHR*;
present_queue: VkQueue*;

create_surface() {
    #if os == OS.Linux {
        surface_create_info: VkXlibSurfaceCreateInfoKHR = {
            dpy = window.handle;
            window = window.window;
        }

        result := vkCreateXlibSurfaceKHR(instance, &surface_create_info, null, &surface);
    }

    if result != VkResult.VK_SUCCESS {
        printf("Unable to create window surface %d\n", result);
        exit(1);
    }
}

struct Window {
    handle: void*;
    window: u64;
    graphics_context: void*;
    width: s32;
    height: s32;
}

window: Window;

#if os == OS.Linux {
    create_window() {
        XInitThreads();

        display := XOpenDisplay(null);
        screen := XDefaultScreen(display);
        black := XBlackPixel(display, screen);
        white := XWhitePixel(display, screen);

        default_window := XDefaultRootWindow(display);
        window.width = 960;
        window.height = 720;
        x_win := XCreateSimpleWindow(display, default_window, 0, 0, window.width, window.height, 0, white, black);
        XSetStandardProperties(display, x_win, "Vulkan Window", "", 0, null, 0, null);

        XSelectInput(display, x_win, XInputMasks.ButtonPressMask|XInputMasks.KeyPressMask|XInputMasks.ExposureMask|XInputMasks.StructureNotifyMask);

        gc := XCreateGC(display, x_win, 0, null);

        XSetBackground(display, gc, white);
        XSetForeground(display, gc, black);

        XClearWindow(display, x_win);
        XMapRaised(display, x_win);

        window.handle = display;
        window.window = x_win;
        window.graphics_context = gc;
    }

    bool handle_inputs() {
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
        return true;
        return false;
    }

    close_window() {
        XFreeGC(window.handle, window.graphics_context);
        XDestroyWindow(window.handle, window.window);
        XCloseDisplay(window.handle);
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
        printf("Unable to create swap chain %d\n", result);
        exit(1);
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

    view_create_info.subresourceRange.aspectMask = VkImageAspectFlagBits.VK_IMAGE_ASPECT_COLOR_BIT;
    view_create_info.subresourceRange.baseMipLevel = 0;
    view_create_info.subresourceRange.levelCount = 1;
    view_create_info.subresourceRange.baseArrayLayer = 0;
    view_create_info.subresourceRange.layerCount = 1;

    each image, i in swap_chain_images {
        view_create_info.image = image;

        result := vkCreateImageView(device, &view_create_info, null, &swap_chain_image_views[i]);
        if result != VkResult.VK_SUCCESS {
            printf("Unable to create image view %d\n", result);
            exit(1);
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

    attribute_descriptions: Array<VkVertexInputAttributeDescription>[2];

    attribute_descriptions[0].binding = 0;
    attribute_descriptions[0].location = 0;
    attribute_descriptions[0].format = VkFormat.VK_FORMAT_R32G32B32_SFLOAT;
    attribute_descriptions[0].offset = 0;

    attribute_descriptions[1].binding = 0;
    attribute_descriptions[1].location = 1;
    attribute_descriptions[1].format = VkFormat.VK_FORMAT_R32G32B32_SFLOAT;
    attribute_descriptions[1].offset = size_of(Vector3);


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
        frontFace = VkFrontFace.VK_FRONT_FACE_CLOCKWISE;
        depthBiasEnable = VK_FALSE;
        depthBiasConstantFactor = 0.0; // Optional
        depthBiasClamp = 0.0;          // Optional
        depthBiasSlopeFactor = 0.0;    // Optional
    }

    multisampling: VkPipelineMultisampleStateCreateInfo = {
        sampleShadingEnable = VK_FALSE;
        rasterizationSamples = VkSampleCountFlagBits.VK_SAMPLE_COUNT_1_BIT;
        minSampleShading = 1.0;           // Optional
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
        printf("Unable to create pipeline layout %d\n", result);
        exit(1);
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
        pDepthStencilState = null;
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
        printf("Unable to create graphics pipeline %d\n", result);
        exit(1);
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
        printf("Unable to create shader module %d\n", result);
        exit(1);
    }

    return shader_module;
}


// Part 11: https://vulkan-tutorial.com/Drawing_a_triangle/Graphics_pipeline_basics/Render_passes
render_pass: VkRenderPass*;

create_render_pass() {
    color_attachment: VkAttachmentDescription = {
        format = swap_chain_format;
        samples = VkSampleCountFlagBits.VK_SAMPLE_COUNT_1_BIT;
        loadOp = VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_CLEAR;
        storeOp = VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_STORE;
        stencilLoadOp = VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        stencilStoreOp = VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_DONT_CARE;
        initialLayout = VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED;
        finalLayout = VkImageLayout.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
    }

    color_attachment_ref: VkAttachmentReference = {
        attachment = 0;
        layout = VkImageLayout.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    }

    subpass: VkSubpassDescription = {
        pipelineBindPoint = VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_GRAPHICS;
        colorAttachmentCount = 1;
        pColorAttachments = &color_attachment_ref;
    }

    dependency: VkSubpassDependency = {
        srcSubpass = 0xFFFFFFFF;
        dstSubpass = 0;
        srcStageMask = cast(u32, VkPipelineStageFlagBits.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT);
        srcAccessMask = 0;
        dstStageMask = cast(u32, VkPipelineStageFlagBits.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT);
        dstAccessMask = cast(u32, VkAccessFlagBits.VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT);
    }

    render_pass_info: VkRenderPassCreateInfo = {
        attachmentCount = 1;
        pAttachments = &color_attachment;
        subpassCount = 1;
        pSubpasses = &subpass;
        dependencyCount = 1;
        pDependencies = &dependency;
    }

    result := vkCreateRenderPass(device, &render_pass_info, null, &render_pass);
    if result != VkResult.VK_SUCCESS {
        printf("Unable to create render pass %d\n", result);
        exit(1);
    }
}


// Part 13: https://vulkan-tutorial.com/en/Drawing_a_triangle/Drawing/Framebuffers
swap_chain_framebuffers: Array<VkFramebuffer*>;

create_framebuffers() {
    array_reserve(&swap_chain_framebuffers, swap_chain_image_views.length);

    framebuffer_info: VkFramebufferCreateInfo = {
        renderPass = render_pass;
        attachmentCount = 1;
        width = swap_chain_extent.width;
        height = swap_chain_extent.height;
        layers = 1;
    }

    each image_view, i in swap_chain_image_views {
        framebuffer_info.pAttachments = &image_view;

        result := vkCreateFramebuffer(device, &framebuffer_info, null, &swap_chain_framebuffers[i]);
        if result != VkResult.VK_SUCCESS {
            printf("Unable to create framebuffer %d\n", result);
            exit(1);
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
        printf("Unable to create command pool %d\n", result);
        exit(1);
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
        printf("Unable to create command pool %d\n", result);
        exit(1);
    }

    begin_info: VkCommandBufferBeginInfo = {
        pInheritanceInfo = null;
    }

    clear_color: VkClearValue;
    clear_color.color.float32[0] = 0.0;
    clear_color.color.float32[1] = 0.0;
    clear_color.color.float32[2] = 0.0;
    clear_color.color.float32[3] = 1.0;

    render_pass_info: VkRenderPassBeginInfo = {
        renderPass = render_pass;
        clearValueCount = 1;
        pClearValues = &clear_color;
    }
    render_pass_info.renderArea.extent = swap_chain_extent;

    each command_buffer, i in command_buffers {
        result = vkBeginCommandBuffer(command_buffer, &begin_info);
        if result != VkResult.VK_SUCCESS {
            printf("Unable to begin recording command buffer %d\n", result);
            exit(1);
        }

        render_pass_info.framebuffer = swap_chain_framebuffers[i];
        vkCmdBeginRenderPass(command_buffer, &render_pass_info, VkSubpassContents.VK_SUBPASS_CONTENTS_INLINE);

        vkCmdBindPipeline(command_buffer, VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_GRAPHICS, graphics_pipeline);

        offset: u64;
        vkCmdBindVertexBuffers(command_buffer, 0, 1, &vertex_buffer, &offset);

        vkCmdBindIndexBuffer(command_buffer, index_buffer, 0, VkIndexType.VK_INDEX_TYPE_UINT32);

        vkCmdDrawIndexed(command_buffer, indices.length, 1, 0, 0, 0);

        vkCmdEndRenderPass(command_buffer);

        result = vkEndCommandBuffer(command_buffer);
        if result != VkResult.VK_SUCCESS {
            printf("Unable to record command buffer %d\n", result);
            exit(1);
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
            printf("Unable to create semaphore %d\n", result);
            exit(1);
        }

        result = vkCreateSemaphore(device, &semaphore_info, null, &render_finished_semaphores[i]);
        if result != VkResult.VK_SUCCESS {
            printf("Unable to create semaphore %d\n", result);
            exit(1);
        }

        result = vkCreateFence(device, &fence_info, null, &in_flight_fences[i]);
        if result != VkResult.VK_SUCCESS {
            printf("Unable to create fence %d\n", result);
            exit(1);
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
        printf("Failed to acquire swap chain image %d\n", result);
        exit(1);
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
        printf("Failed to submit draw command buffer %d\n", result);
        exit(1);
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
        printf("Failed to present swap chain image %d\n", result);
        exit(1);
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
    create_framebuffers();
    create_uniform_buffers();
    create_command_buffers();
}

cleanup_swap_chain() {
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
vertices: Array<Vertex>[4];

setup_vertices() {
    position: Vector3 = { x = -0.5; y = -0.5; }
    color: Vector3 = { x = 1.0; y = 0.0; z = 0.0; }

    vertices[0].position = position;
    vertices[0].color = color;

    position.x = 0.5;
    color.x = 0.0;
    color.y = 1.0;

    vertices[1].position = position;
    vertices[1].color = color;

    position.y = 0.5;
    color.y = 0.0;
    color.z = 1.0;

    vertices[2].position = position;
    vertices[2].color = color;

    position.x = -0.5;
    color.x = 1.0;
    color.y = 1.0;

    vertices[3].position = position;
    vertices[3].color = color;
}

create_vertex_buffer() {
    size := size_of(Vertex) * vertices.length;

    staging_buffer: VkBuffer*;
    staging_buffer_memory: VkDeviceMemory*;
    create_buffer(size, VkBufferUsageFlagBits.VK_BUFFER_USAGE_TRANSFER_SRC_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, &staging_buffer, &staging_buffer_memory);

    data: void*;
    vkMapMemory(device, staging_buffer_memory, 0, size, 0, &data);
    memcpy(data, vertices.data, size);
    vkUnmapMemory(device, staging_buffer_memory);

    create_buffer(size, VkBufferUsageFlagBits.VK_BUFFER_USAGE_TRANSFER_DST_BIT | VkBufferUsageFlagBits.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, &vertex_buffer, &vertex_buffer_memory);

    copy_buffer(staging_buffer, vertex_buffer, size);

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

    printf("Failed to find a suitable memory type\n");
    exit(1);
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
        printf("Unable to create buffer %d\n", result);
        exit(1);
    }

    memory_requirements: VkMemoryRequirements;
    vkGetBufferMemoryRequirements(device, *buffer, &memory_requirements);

    alloc_info: VkMemoryAllocateInfo = {
        allocationSize = memory_requirements.size;
        memoryTypeIndex = find_memory_type(memory_requirements.memoryTypeBits, properties);
    }

    result = vkAllocateMemory(device, &alloc_info, null, buffer_memory);
    if result != VkResult.VK_SUCCESS {
        printf("Unable to allocate buffer memory %d\n", result);
        exit(1);
    }

    vkBindBufferMemory(device, *buffer, *buffer_memory, 0);
}

copy_buffer(VkBuffer* source_buffer, VkBuffer* dest_buffer, u64 size) {
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

    copy_region: VkBufferCopy = {
        srcOffset = 0;
        dstOffset = 0;
        size = size;
    }

    vkCmdCopyBuffer(command_buffer, source_buffer, dest_buffer, 1, &copy_region);

    vkEndCommandBuffer(command_buffer);

    submit_info: VkSubmitInfo = {
        commandBufferCount = 1;
        pCommandBuffers = &command_buffer;
    }

    vkQueueSubmit(graphics_queue, 1, &submit_info, null);
    vkQueueWaitIdle(graphics_queue);

    vkFreeCommandBuffers(device, command_pool, 1, &command_buffer);
}


// Part 20: https://vulkan-tutorial.com/en/Vertex_buffers/Index_buffer
index_buffer: VkBuffer*;
index_buffer_memory: VkDeviceMemory*;
indices: Array<u32> = [0, 1, 2, 2, 3, 0]

create_index_buffer() {
    size := size_of(u32) * indices.length;

    staging_buffer: VkBuffer*;
    staging_buffer_memory: VkDeviceMemory*;
    create_buffer(size, VkBufferUsageFlagBits.VK_BUFFER_USAGE_TRANSFER_SRC_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, &staging_buffer, &staging_buffer_memory);

    data: void*;
    vkMapMemory(device, staging_buffer_memory, 0, size, 0, &data);
    memcpy(data, indices.data, size);
    vkUnmapMemory(device, staging_buffer_memory);

    create_buffer(size, VkBufferUsageFlagBits.VK_BUFFER_USAGE_TRANSFER_DST_BIT | VkBufferUsageFlagBits.VK_BUFFER_USAGE_INDEX_BUFFER_BIT, VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, &index_buffer, &index_buffer_memory);

    copy_buffer(staging_buffer, index_buffer, size);

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
    layout_binding: VkDescriptorSetLayoutBinding = {
        binding = 0;
        descriptorType = VkDescriptorType.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
        descriptorCount = 1;
        stageFlags = VkShaderStageFlagBits.VK_SHADER_STAGE_VERTEX_BIT;
        pImmutableSamplers = null; // Optional
    }

    layout_info: VkDescriptorSetLayoutCreateInfo = {
        bindingCount = 1;
        pBindings = &layout_binding;
    }

    result := vkCreateDescriptorSetLayout(device, &layout_info, null, &descriptor_set_layout);
    if result != VkResult.VK_SUCCESS {
        printf("Failed to create descriptor set layout %d", result);
        exit(1);
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

update_uniform_buffer(u32 current_image) {
    ubo: UniformBufferObject = {
        model = mat4_rotate_z(radians(45.0));
    }
    size := size_of(ubo);

    data: void*;
    vkMapMemory(device, uniform_buffers_memory[current_image], 0, size, 0, &data);
    memcpy(data, &ubo, size);
    vkUnmapMemory(device, uniform_buffers_memory[current_image]);
}

pi := 3.14159265359; #const

float radians(float degrees) {
    return degrees * pi / 180;
}

Matrix4 mat4_ident() {
    matrix: Matrix4 = {
        a = vec4(x = 1.0);
        b = vec4(y = 1.0);
        c = vec4(z = 1.0);
        d = vec4(w = 1.0);
    }
    return matrix;
}

Vector4 vec4(float x = 0.0, float y = 0.0, float z = 0.0, float w = 0.0) {
    vector: Vector4 = { x = x; y = y; z = z; w = w; }
    return vector;
}

// rotate, look_at, and perspective borrowed from https://github.com/g-truc/glm
Matrix4 mat4_rotate_z(float angle) {
    sin := sin(angle);
    cos := cos(angle);

    matrix := mat4_ident();
    matrix.a.x = cos;
    matrix.a.y = sin;
    matrix.b.x = -sin;
    matrix.b.y = cos;

    return matrix;
}

float sin(float angle) #extern "m-2.33"
float cos(float angle) #extern "m-2.33"


#run main();
