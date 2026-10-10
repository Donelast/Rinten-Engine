//Times memcpy into write-combined host-visible scratch memory versus ordinary heap memory

#include <vulkan/vulkan.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#define CHECK(x) do { VkResult _r = (x); if (_r != VK_SUCCESS) { \
	fprintf(stderr, "%s failed at line %d: %d\n", #x, __LINE__, _r); exit(1); } } while (0)

static double now_ns(void)
{
	struct timespec t;
	clock_gettime(CLOCK_MONOTONIC, &t);
	return (double)t.tv_sec * 1e9 + (double)t.tv_nsec;
}

static double median(double *v, int n)
{
	for (int i = 1; i < n; i++) {
		double k = v[i]; int j = i - 1;
		while (j >= 0 && v[j] > k) { v[j + 1] = v[j]; j--; }
		v[j + 1] = k;
	}
	return v[n / 2];
}

#define TRIALS 9
#define CAPACITY (16u * 1024u * 1024u)

int main(void)
{
	VkApplicationInfo app = { .sType = VK_STRUCTURE_TYPE_APPLICATION_INFO,
		.pApplicationName = "vkwrite", .apiVersion = VK_API_VERSION_1_3 };
	VkInstanceCreateInfo ici = { .sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO, .pApplicationInfo = &app };
	VkInstance instance;
	CHECK(vkCreateInstance(&ici, NULL, &instance));

	uint32_t count = 0;
	CHECK(vkEnumeratePhysicalDevices(instance, &count, NULL));
	VkPhysicalDevice *devices = calloc(count, sizeof(*devices));
	CHECK(vkEnumeratePhysicalDevices(instance, &count, devices));
	VkPhysicalDevice physical = devices[0];
	for (uint32_t i = 0; i < count; i++) {
		VkPhysicalDeviceProperties p;
		vkGetPhysicalDeviceProperties(devices[i], &p);
		if (p.deviceType == VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU) { physical = devices[i]; break; }
	}

	VkPhysicalDeviceProperties props;
	vkGetPhysicalDeviceProperties(physical, &props);
	printf("device: %s\n\n", props.deviceName);

	VkPhysicalDeviceMemoryProperties mem;
	vkGetPhysicalDeviceMemoryProperties(physical, &mem);

	printf("memory types:\n");
	for (uint32_t i = 0; i < mem.memoryTypeCount; i++) {
		VkMemoryPropertyFlags f = mem.memoryTypes[i].propertyFlags;
		printf("  %2u  heap %u  %6.0f MB  %s%s%s%s\n", i, mem.memoryTypes[i].heapIndex,
			mem.memoryHeaps[mem.memoryTypes[i].heapIndex].size / 1048576.0,
			(f & VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT) ? "DEVICE_LOCAL " : "",
			(f & VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) ? "HOST_VISIBLE " : "",
			(f & VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) ? "HOST_COHERENT " : "",
			(f & VK_MEMORY_PROPERTY_HOST_CACHED_BIT) ? "HOST_CACHED " : "");
	}
	printf("\n");

	uint32_t families = 0;
	vkGetPhysicalDeviceQueueFamilyProperties(physical, &families, NULL);
	VkQueueFamilyProperties *fp = calloc(families, sizeof(*fp));
	vkGetPhysicalDeviceQueueFamilyProperties(physical, &families, fp);
	uint32_t family = 0;
	for (uint32_t i = 0; i < families; i++)
		if (fp[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) { family = i; break; }

	float priority = 1.0f;
	VkDeviceQueueCreateInfo qci = { .sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,
		.queueFamilyIndex = family, .queueCount = 1, .pQueuePriorities = &priority };
	VkDeviceCreateInfo dci = { .sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,
		.queueCreateInfoCount = 1, .pQueueCreateInfos = &qci };
	VkDevice device;
	CHECK(vkCreateDevice(physical, &dci, NULL, &device));

	VkBufferCreateInfo bci = { .sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO, .size = CAPACITY,
		.usage = VK_BUFFER_USAGE_VERTEX_BUFFER_BIT | VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
		.sharingMode = VK_SHARING_MODE_EXCLUSIVE };
	VkBuffer buffer;
	CHECK(vkCreateBuffer(device, &bci, NULL, &buffer));

	VkMemoryRequirements need;
	vkGetBufferMemoryRequirements(device, buffer, &need);

	//The engine's first choice, then its fallback - see VkFrame.EnsureScratch.
	const VkMemoryPropertyFlags wanted[2] = {
		VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT | VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
		VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
	};
	const char *names[2] = { "device-local host-visible (what the scratch gets)", "plain host-visible (the fallback)" };

	unsigned char *source = aligned_alloc(64, CAPACITY);
	memset(source, 0x33, CAPACITY);
	unsigned char *control = aligned_alloc(64, CAPACITY);
	memset(control, 0, CAPACITY);

	const size_t sizes[] = { 1024, 4096, 16384, 65536, 262144, 1048576, 4194304 };
	const int howMany = (int)(sizeof(sizes) / sizeof(*sizes));
	double best[TRIALS];

	for (int which = 0; which < 2; which++) {
		uint32_t type = UINT32_MAX;
		for (uint32_t i = 0; i < mem.memoryTypeCount; i++)
			if ((need.memoryTypeBits & (1u << i)) &&
				(mem.memoryTypes[i].propertyFlags & wanted[which]) == wanted[which]) { type = i; break; }

		if (type == UINT32_MAX) { printf("%s: no such memory type\n\n", names[which]); continue; }

		VkMemoryAllocateInfo mai = { .sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
			.allocationSize = need.size, .memoryTypeIndex = type };
		VkDeviceMemory memory;
		if (vkAllocateMemory(device, &mai, NULL, &memory) != VK_SUCCESS) {
			printf("%s: would not allocate %u MB\n\n", names[which], CAPACITY / 1048576u);
			continue;
		}

		void *mapped = NULL;
		CHECK(vkMapMemory(device, memory, 0, VK_WHOLE_SIZE, 0, &mapped));

		printf("%s - memory type %u\n", names[which], type);

		for (int s = 0; s < howMany; s++) {
			size_t bytes = sizes[s];
			int reps = (int)(64u * 1024u * 1024u / bytes);
			if (reps < 4) reps = 4;

			for (int t = 0; t < TRIALS; t++) {
				double t0 = now_ns();
				for (int i = 0; i < reps; i++)
					memcpy((char *)mapped + ((size_t)i * bytes) % (CAPACITY - bytes), source, bytes);
				double t1 = now_ns();
				best[t] = (t1 - t0) / reps;
			}
			double ns = median(best, TRIALS);
			printf("  %7zu B  %8.2f us  %6.2f GB/s\n", bytes, ns / 1000.0, bytes / ns);
		}

		vkUnmapMemory(device, memory);
		vkFreeMemory(device, memory, NULL);
		printf("\n");
	}

	printf("ordinary heap memory, the same copies\n");
	for (int s = 0; s < howMany; s++) {
		size_t bytes = sizes[s];
		int reps = (int)(64u * 1024u * 1024u / bytes);
		if (reps < 4) reps = 4;

		for (int t = 0; t < TRIALS; t++) {
			double t0 = now_ns();
			for (int i = 0; i < reps; i++)
				memcpy(control + ((size_t)i * bytes) % (CAPACITY - bytes), source, bytes);
			double t1 = now_ns();
			best[t] = (t1 - t0) / reps;
		}
		double ns = median(best, TRIALS);
		printf("  %7zu B  %8.2f us  %6.2f GB/s\n", bytes, ns / 1000.0, bytes / ns);
	}

	return 0;
}
