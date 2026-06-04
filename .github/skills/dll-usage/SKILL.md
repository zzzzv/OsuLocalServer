---
name: dll-usage
description: 'Figure out how to use a third-party DLL library. Use when: needing to call APIs from a NuGet or DLL dependency, understand types/methods in an external library.'
---

# DLL 使用指南

## 使用时机
- 需要调用 NuGet 包或 DLL 引用的方法时
- 需要了解库暴露的类型、枚举或方法时
- 希望避免对 DLL 内部的猜测和试错时

## 优先级顺序

### 1. 优先查文档
先去该库的 GitHub 仓库查看 README、`docs/` 文件夹和示例代码，通常能覆盖 90% 的需求。

### 2. 询问用户
如果文档不清晰，直接询问用户——他们可能对该库有经验或知道文档位置。

### 3. 最后手段：检查 DLL
仅在无文档且用户也无法帮助的情况下，使用反射或反编译工具查看 DLL 的公共 API。
