# Console Calculator Blueprint

**ID:** `otter-official/console-calculator`  
**Version:** `1.0.0`  
**Targets:** `console`  
**Dependencies:** `otter-official/math-utils` (v1.0.0)

## Overview

Demonstrates range clamping and mathematical boundary enforcement using `otter-official/math-utils`.

```mermaid
graph LR
    inputVal[Number Input] -->|val| clampOperation[Clamp in Range]
    clampOperation -->|clamped| displayResult[Display Result]
```
