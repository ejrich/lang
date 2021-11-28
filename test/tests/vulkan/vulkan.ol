#import vulkan

// This test follows vulkan-tutorial.com

main() {
    create_window();

    init_vulkan();

    cleanup();
}

init_vulkan() {
    create_instance();

    setup_debug_messenger();

    create_surface();

    pick_physical_device();

    create_logical_device();
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

    if features.geometryShader == VK_FALSE score = 0;

    _: u32;
    if !find_queue_families(device, &_, &_) score = 0;

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

    device_create_info: VkDeviceCreateInfo = { pEnabledFeatures = &features; }

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
}

window: Window;

#if os == OS.Linux {
    create_window() {
        display := XOpenDisplay(null);
        screen := XDefaultScreen(display);
        black := XBlackPixel(display, screen);
        white := XWhitePixel(display, screen);

        default_window := XDefaultRootWindow(display);
        x_win := XCreateSimpleWindow(display, default_window, 0, 0, 1280, 720, 0, white, black);
        XSetStandardProperties(display, x_win, "Vulkan Window", "", 0, null, 0, null);

        XSelectInput(display, x_win, XInputMasks.ExposureMask|XInputMasks.ButtonPressMask|XInputMasks.KeyPressMask);

        gc := XCreateGC(display, x_win, 0, null);

        XSetBackground(display, gc, white);
        XSetForeground(display, gc, black);

        XClearWindow(display, x_win);
        XMapRaised(display, x_win);

        XSync(display, false);

        window.handle = display;
        window.window = x_win;
        window.graphics_context = gc;
    }

    close_window() {
        XFreeGC(window.handle, window.graphics_context);
        XDestroyWindow(window.handle, window.window);
        XCloseDisplay(window.handle);
    }
}

#run main();
