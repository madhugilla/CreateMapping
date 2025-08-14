# Enhanced Mapping Output Example

This shows the improved output from the enhanced AI mapping system.

## Sample Input Tables

### SQL Source Table: `orders`
```sql
CREATE TABLE orders (
    order_id INT PRIMARY KEY,
    customer_name NVARCHAR(100) NOT NULL,
    order_total DECIMAL(18,2),
    order_date DATETIME NOT NULL,
    created_date DATETIME DEFAULT GETDATE(),
    created_by NVARCHAR(50),
    last_modified DATETIME,
    modified_by NVARCHAR(50),
    status NVARCHAR(20)
);
```

### Dataverse Target Table: `sale_order`
Fields include custom business fields and standard system fields:
- `sale_orderid` (Primary ID - Custom)
- `customername` (Custom business field)  
- `totalamount` (Custom business field)
- `orderdate` (Custom business field)
- `createdon` (System audit field)
- `createdby` (System audit field)
- `modifiedon` (System audit field)
- `modifiedby` (System audit field)
- `statecode` (System state field)

## Enhanced AI Mapping Output

### Priority-Ordered Mapping Results

#### ✅ ACCEPTED MAPPINGS (High Confidence ≥ 0.70)

| Priority | Source Column | Target Column | Confidence | Match Type | Transformation | Rationale |
|----------|---------------|---------------|------------|------------|----------------|-----------|
| 1 | `order_id` | `sale_orderid` | **0.946** | `AI-Custom` | None | Primary key mapping with exact semantic match. Custom field priority boost applied. |
| 1 | `customer_name` | `customername` | **0.882** | `AI-Custom` | None | Direct customer name mapping. Custom business field with high semantic similarity. |
| 1 | `order_total` | `totalamount` | **0.840** | `AI-Custom` | None | Financial amount mapping with compatible decimal types. Business logic match. |
| 1 | `order_date` | `orderdate` | **0.840** | `AI-Custom` | None | Date field mapping with clear business purpose alignment. |
| 2 | `created_date` | `createdon` | **0.713** | `AI-System-CreatedOn` | None | System audit field pattern: created_date → createdon. Standard Dataverse audit mapping. |
| 2 | `created_by` | `createdby` | **0.713** | `AI-System-CreatedBy` | None | System audit field pattern: created_by → createdby. User tracking field. |

#### 🔍 NEEDS REVIEW (Medium Confidence 0.40-0.69)

| Priority | Source Column | Target Column | Confidence | Match Type | Transformation | Rationale |
|----------|---------------|---------------|------------|------------|----------------|-----------|
| 2 | `last_modified` | `modifiedon` | **0.665** | `AI-System-ModifiedOn` | None | System audit field pattern with slight name variation. Requires verification. |
| 2 | `modified_by` | `modifiedby` | **0.665** | `AI-System-ModifiedBy` | None | System audit field pattern. User lookup field mapping needs validation. |
| 3 | `status` | `statecode` | **0.618** | `AI-System-State` | Value mapping required | Status enumeration mapping. May need value transformation logic. |

#### ❌ UNRESOLVED SOURCE COLUMNS
None - All source columns mapped!

#### ❓ UNUSED TARGET COLUMNS  
None - Efficient mapping achieved!

## Key Enhancements Demonstrated

### 1. **Priority-Based Ordering**
- Custom business fields (Priority 1) mapped first
- System fields (Priority 2-3) mapped by importance
- Clear visual distinction in output

### 2. **Enhanced Match Types**
- `AI-Custom`: Custom business field mappings
- `AI-System-{Type}`: System field with classification
- Clear indication of field category

### 3. **Confidence Adjustments**
- Custom fields: +5% boost (0.80 → 0.84)
- System fields: -5% adjustment (0.75 → 0.713)
- Transparent confidence calculation

### 4. **Detailed Rationale**
- Business logic explanation
- System pattern recognition
- Transformation requirements
- Data type compatibility notes

### 5. **Systematic Classification**
Each mapping includes:
- **Priority Level**: Processing order
- **Field Type**: Custom vs System classification  
- **Confidence Score**: AI reasoning confidence
- **Transformation**: Required data conversions
- **Rationale**: Detailed mapping logic

This enhanced output provides clear visibility into the AI's reasoning process and helps users understand both the mapping decisions and the prioritization logic.