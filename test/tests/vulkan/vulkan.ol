#import vulkan

main() {
    create_instance();

    cleanup();
}

instance: VkInstance*;

create_instance() {
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

    extension_count: u32;
    result := vkEnumerateInstanceExtensionProperties(null, &extension_count, null);
    if result != VkResult.VK_SUCCESS {
        printf("Unable to get vulkan extensions\n");
        exit(1);
    }

    extensions: Array<VkExtensionProperties>[extension_count];
    vkEnumerateInstanceExtensionProperties(null, &extension_count, extensions.data);

    extension_names: Array<u8*>[extension_count];
    each extension, i in extensions {
        extension_names[i] = &extension.extensionName;
    }

    instance_create_info: VkInstanceCreateInfo = {
        pApplicationInfo = &app_info;
        enabledExtensionCount = extension_count;
        ppEnabledExtensionNames = extension_names.data;
    }

    printf("Creating vulkan instance\n");
    if (vkCreateInstance(&instance_create_info, null, &instance) != VkResult.VK_SUCCESS) {
        printf("Unable to create vulkan instance\n");
        exit(1);
    }
}

cleanup() {
    vkDestroyInstance(instance, null);
}

#run main();
