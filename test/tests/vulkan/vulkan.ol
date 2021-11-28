#import vulkan

// This test follows vulkan-tutorial.com

main() {
    create_instance();

    setup_debug_messenger();

    cleanup();
}

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
    printf("Validation Layer - %s\n", callback_data.pMessage);

    return VK_FALSE;
}

cleanup() {
    if enable_validation_layers {
        func: PFN_vkDestroyDebugUtilsMessengerEXT = vkGetInstanceProcAddr(instance, "vkDestroyDebugUtilsMessengerEXT");
        if func != null {
            func(instance, debug_messenger, null);
        }
    }

    vkDestroyInstance(instance, null);
}

#run main();
