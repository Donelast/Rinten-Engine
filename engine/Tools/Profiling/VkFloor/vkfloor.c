//Times per-draw Vulkan command recording (no submit), via loader trampoline vs device pointers

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

static VkInstance instance;
static VkPhysicalDevice physical;
static VkDevice device;
static uint32_t queueFamily;
static VkCommandPool pool;
static VkCommandBuffer cmd;
static VkPipelineLayout layout;
static VkDescriptorSet set;
static VkBuffer buffer;

typedef void (VKAPI_PTR *PFN_push)(VkCommandBuffer, VkPipelineLayout, VkShaderStageFlags, uint32_t, uint32_t, const void *);
typedef void (VKAPI_PTR *PFN_sets)(VkCommandBuffer, VkPipelineBindPoint, VkPipelineLayout, uint32_t, uint32_t, const VkDescriptorSet *, uint32_t, const uint32_t *);
typedef void (VKAPI_PTR *PFN_viewport)(VkCommandBuffer, uint32_t, uint32_t, const VkViewport *);
typedef void (VKAPI_PTR *PFN_scissor)(VkCommandBuffer, uint32_t, uint32_t, const VkRect2D *);
typedef void (VKAPI_PTR *PFN_vertex)(VkCommandBuffer, uint32_t, uint32_t, const VkBuffer *, const VkDeviceSize *);
typedef void (VKAPI_PTR *PFN_index)(VkCommandBuffer, VkBuffer, VkDeviceSize, VkIndexType);

//Device-level entry points, the way the engine reaches them.
static PFN_push d_push;
static PFN_sets d_sets;
static PFN_viewport d_viewport;
static PFN_scissor d_scissor;
static PFN_vertex d_vertex;
static PFN_index d_index;

static void setup(void)
{
	VkApplicationInfo app = { .sType = VK_STRUCTURE_TYPE_APPLICATION_INFO,
		.pApplicationName = "vkfloor", .apiVersion = VK_API_VERSION_1_3 };
	VkInstanceCreateInfo ici = { .sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO, .pApplicationInfo = &app };
	CHECK(vkCreateInstance(&ici, NULL, &instance));

	uint32_t count = 0;
	CHECK(vkEnumeratePhysicalDevices(instance, &count, NULL));
	if (count == 0) { fprintf(stderr, "no physical device\n"); exit(1); }
	VkPhysicalDevice *devices = calloc(count, sizeof(*devices));
	CHECK(vkEnumeratePhysicalDevices(instance, &count, devices));

	//The one the engine would take: a discrete card if there is one.
	physical = devices[0];
	for (uint32_t i = 0; i < count; i++) {
		VkPhysicalDeviceProperties p;
		vkGetPhysicalDeviceProperties(devices[i], &p);
		if (p.deviceType == VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU) { physical = devices[i]; break; }
	}

	VkPhysicalDeviceProperties props;
	vkGetPhysicalDeviceProperties(physical, &props);
	printf("device: %s (driver %u, api %u.%u.%u)\n\n", props.deviceName, props.driverVersion,
		VK_VERSION_MAJOR(props.apiVersion), VK_VERSION_MINOR(props.apiVersion), VK_VERSION_PATCH(props.apiVersion));

	uint32_t families = 0;
	vkGetPhysicalDeviceQueueFamilyProperties(physical, &families, NULL);
	VkQueueFamilyProperties *fp = calloc(families, sizeof(*fp));
	vkGetPhysicalDeviceQueueFamilyProperties(physical, &families, fp);
	queueFamily = 0;
	for (uint32_t i = 0; i < families; i++)
		if (fp[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) { queueFamily = i; break; }

	float priority = 1.0f;
	VkDeviceQueueCreateInfo qci = { .sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,
		.queueFamilyIndex = queueFamily, .queueCount = 1, .pQueuePriorities = &priority };
	VkDeviceCreateInfo dci = { .sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,
		.queueCreateInfoCount = 1, .pQueueCreateInfos = &qci };
	CHECK(vkCreateDevice(physical, &dci, NULL, &device));

	VkCommandPoolCreateInfo pci = { .sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,
		.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT, .queueFamilyIndex = queueFamily };
	CHECK(vkCreateCommandPool(device, &pci, NULL, &pool));

	VkCommandBufferAllocateInfo cai = { .sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,
		.commandPool = pool, .level = VK_COMMAND_BUFFER_LEVEL_PRIMARY, .commandBufferCount = 1 };
	CHECK(vkAllocateCommandBuffers(device, &cai, &cmd));

	//A layout shaped like the engine's: four sets and 128 bytes of push range.
	VkDescriptorSetLayoutBinding binding = { .binding = 0,
		.descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, .descriptorCount = 1,
		.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT };
	VkDescriptorSetLayoutCreateInfo sli = { .sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO,
		.bindingCount = 1, .pBindings = &binding };
	VkDescriptorSetLayout setLayout;
	CHECK(vkCreateDescriptorSetLayout(device, &sli, NULL, &setLayout));

	VkDescriptorSetLayout four[4] = { setLayout, setLayout, setLayout, setLayout };
	VkPushConstantRange range = { .stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
		.offset = 0, .size = 128 };
	VkPipelineLayoutCreateInfo pli = { .sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO,
		.setLayoutCount = 4, .pSetLayouts = four, .pushConstantRangeCount = 1, .pPushConstantRanges = &range };
	CHECK(vkCreatePipelineLayout(device, &pli, NULL, &layout));

	VkDescriptorPoolSize size = { .type = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, .descriptorCount = 4 };
	VkDescriptorPoolCreateInfo dpi = { .sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO,
		.maxSets = 4, .poolSizeCount = 1, .pPoolSizes = &size };
	VkDescriptorPool descriptorPool;
	CHECK(vkCreateDescriptorPool(device, &dpi, NULL, &descriptorPool));

	VkDescriptorSetAllocateInfo dsi = { .sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO,
		.descriptorPool = descriptorPool, .descriptorSetCount = 1, .pSetLayouts = &setLayout };
	CHECK(vkAllocateDescriptorSets(device, &dsi, &set));

	VkBufferCreateInfo bci = { .sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO, .size = 4096,
		.usage = VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT | VK_BUFFER_USAGE_VERTEX_BUFFER_BIT | VK_BUFFER_USAGE_INDEX_BUFFER_BIT,
		.sharingMode = VK_SHARING_MODE_EXCLUSIVE };
	CHECK(vkCreateBuffer(device, &bci, NULL, &buffer));

	VkMemoryRequirements need;
	vkGetBufferMemoryRequirements(device, buffer, &need);
	VkPhysicalDeviceMemoryProperties mem;
	vkGetPhysicalDeviceMemoryProperties(physical, &mem);
	uint32_t type = 0;
	for (uint32_t i = 0; i < mem.memoryTypeCount; i++)
		if ((need.memoryTypeBits & (1u << i)) &&
			(mem.memoryTypes[i].propertyFlags & VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT)) { type = i; break; }
	VkMemoryAllocateInfo mai = { .sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
		.allocationSize = need.size, .memoryTypeIndex = type };
	VkDeviceMemory memory;
	CHECK(vkAllocateMemory(device, &mai, NULL, &memory));
	CHECK(vkBindBufferMemory(device, buffer, memory, 0));

	VkDescriptorBufferInfo dbi = { .buffer = buffer, .offset = 0, .range = 256 };
	VkWriteDescriptorSet write = { .sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET, .dstSet = set,
		.dstBinding = 0, .descriptorCount = 1, .descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,
		.pBufferInfo = &dbi };
	vkUpdateDescriptorSets(device, 1, &write, 0, NULL);

	d_push = (PFN_push)vkGetDeviceProcAddr(device, "vkCmdPushConstants");
	d_sets = (PFN_sets)vkGetDeviceProcAddr(device, "vkCmdBindDescriptorSets");
	d_viewport = (PFN_viewport)vkGetDeviceProcAddr(device, "vkCmdSetViewport");
	d_scissor = (PFN_scissor)vkGetDeviceProcAddr(device, "vkCmdSetScissor");
	d_vertex = (PFN_vertex)vkGetDeviceProcAddr(device, "vkCmdBindVertexBuffers");
	d_index = (PFN_index)vkGetDeviceProcAddr(device, "vkCmdBindIndexBuffer");
}

static void begin(void)
{
	VkCommandBufferBeginInfo bi = { .sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
		.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT };
	CHECK(vkBeginCommandBuffer(cmd, &bi));
}

static void finish(void)
{
	CHECK(vkEndCommandBuffer(cmd));
	CHECK(vkResetCommandPool(device, pool, 0));
}

#define TRIALS 7
#define N 100000

static double best[TRIALS];

//The median of a handful of runs, so one scheduling hiccup does not become the answer. Timed around
//the loop, not around each call: a clock read costs about as much as the calls being measured.
static double median(double *v, int n)
{
	for (int i = 1; i < n; i++) {
		double k = v[i]; int j = i - 1;
		while (j >= 0 && v[j] > k) { v[j + 1] = v[j]; j--; }
		v[j + 1] = k;
	}
	return v[n / 2];
}

#define BENCH(name, count, body) do { \
	for (int t = 0; t < TRIALS; t++) { \
		begin(); \
		double t0 = now_ns(); \
		for (int i = 0; i < (count); i++) { body; } \
		double t1 = now_ns(); \
		finish(); \
		best[t] = (t1 - t0) / (double)(count); \
	} \
	printf("  %-44s %7.1f ns\n", name, median(best, TRIALS)); \
} while (0)

int main(void)
{
	setup();

	unsigned char pushData[128];
	memset(pushData, 0x5a, sizeof(pushData));

	VkViewport viewport = { .x = 0, .y = 0, .width = 1920, .height = 1080, .minDepth = 0, .maxDepth = 1 };
	VkRect2D scissor = { .offset = { 0, 0 }, .extent = { 1920, 1080 } };
	VkDeviceSize zero = 0;

	printf("through the loader's trampoline (what -lvulkan links to):\n");
	BENCH("vkCmdPushConstants 128 B", N, vkCmdPushConstants(cmd, layout, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, 128, pushData));
	BENCH("vkCmdPushConstants 80 B", N, vkCmdPushConstants(cmd, layout, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, 80, pushData));
	BENCH("vkCmdBindDescriptorSets 1 set", N, vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, layout, 0, 1, &set, 0, NULL));
	BENCH("vkCmdSetViewport + vkCmdSetScissor", N, { vkCmdSetViewport(cmd, 0, 1, &viewport); vkCmdSetScissor(cmd, 0, 1, &scissor); });
	BENCH("vkCmdBindVertexBuffers 1", N, vkCmdBindVertexBuffers(cmd, 0, 1, &buffer, &zero));
	BENCH("vkCmdBindIndexBuffer", N, vkCmdBindIndexBuffer(cmd, buffer, 0, VK_INDEX_TYPE_UINT32));

	printf("\nthrough vkGetDeviceProcAddr (what volk and VulkanLoader do):\n");
	BENCH("vkCmdPushConstants 128 B", N, d_push(cmd, layout, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, 128, pushData));
	BENCH("vkCmdPushConstants 80 B", N, d_push(cmd, layout, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, 80, pushData));
	BENCH("vkCmdBindDescriptorSets 1 set", N, d_sets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, layout, 0, 1, &set, 0, NULL));
	BENCH("vkCmdSetViewport + vkCmdSetScissor", N, { d_viewport(cmd, 0, 1, &viewport); d_scissor(cmd, 0, 1, &scissor); });
	BENCH("vkCmdBindVertexBuffers 1", N, d_vertex(cmd, 0, 1, &buffer, &zero));
	BENCH("vkCmdBindIndexBuffer", N, d_index(cmd, buffer, 0, VK_INDEX_TYPE_UINT32));

	printf("\nwhat is around the recording, per call not per command:\n");
	{
		VkCommandBufferBeginInfo bi = { .sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
			.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT };
		for (int t = 0; t < TRIALS; t++) {
			double t0 = now_ns();
			for (int i = 0; i < 2000; i++) {
				vkBeginCommandBuffer(cmd, &bi);
				vkEndCommandBuffer(cmd);
				vkResetCommandPool(device, pool, 0);
			}
			double t1 = now_ns();
			best[t] = (t1 - t0) / 2000.0;
		}
		printf("  %-44s %7.1f ns\n", "begin + end + reset pool, empty", median(best, TRIALS));
	}

	//The floor under all of it: what the loop and the clock cost on their own.
	for (int t = 0; t < TRIALS; t++) {
		begin();
		double t0 = now_ns();
		volatile int sink = 0;
		for (int i = 0; i < N; i++) sink += i;
		double t1 = now_ns();
		finish();
		best[t] = (t1 - t0) / (double)N;
	}
	printf("  %-44s %7.1f ns\n", "empty loop iteration", median(best, TRIALS));

	return 0;
}
