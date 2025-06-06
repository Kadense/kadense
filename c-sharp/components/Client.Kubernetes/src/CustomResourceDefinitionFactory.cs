using k8s.Models;
using System.Diagnostics;
using System.Reflection;

namespace Kadense.Client.Kubernetes
{
    public class CustomResourceDefinitionFactory
    {
        /// <summary>
        /// Processes the properties of a given type and generates a dictionary of JSON schema properties.
        /// </summary>
        /// <param name="type">The type whose properties are to be processed.</param>
        /// <returns>A dictionary mapping property names to their corresponding JSON schema properties.</returns>
        public IDictionary<string, V1JSONSchemaProps> ProcessProperties(Type type)
        {
            var properties = new Dictionary<string, V1JSONSchemaProps>();

            foreach (var property in type.GetProperties())
            {
                if (!this.IgnoreProperty(type, property))
                {
                    string propertyName = property.Name;
                    var jsonPropNames = property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false);
                    if (jsonPropNames.Length > 0)
                    {
                        propertyName = ((JsonPropertyNameAttribute)jsonPropNames[0]).Name;
                    }
                    try
                    {
                        properties.Add(propertyName, ProcessProperty(property));
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Error processing property {property.Name} of type {type.Name}", ex);
                    }
                }
            }

            return properties;
        }

        /// <summary>
        /// Determines whether a property should be ignored during CRD generation.
        /// </summary>
        /// <param name="classType">The type of the class containing the property.</param>
        /// <param name="property">The property to check.</param>
        /// <returns>True if the property should be ignored; otherwise, false.</returns>
        private bool IgnoreProperty(Type classType, PropertyInfo property)
        {
            var ignoreFlags = property.GetCustomAttributes(typeof(IgnoreOnCrdGenerationAttribute), false);
            if (ignoreFlags.Length > 0)
                return true;

            var crdAttributes = classType.GetCustomAttributes(typeof(KubernetesCustomResourceAttribute), false);
            if (crdAttributes.Length > 0)
            {
                switch (property.Name.ToLower())
                {
                    case "apiversion":
                        return true;

                    case "kind":
                        return true;

                    default:
                        return false;
                }
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Processes a property and generates its corresponding JSON schema property.
        /// </summary>
        /// <param name="property">The property to process.</param>
        /// <returns>The JSON schema property for the given property.</returns>
        public V1JSONSchemaProps ProcessProperty(PropertyInfo property)
        {
            return ProcessProperty(property.PropertyType);
        }

        /// <summary>
        /// Processes a type and generates its corresponding JSON schema property.
        /// </summary>
        /// <param name="propertyType">The type to process.</param>
        /// <returns>The JSON schema property for the given type.</returns>
        public V1JSONSchemaProps ProcessProperty(Type propertyType)
        {
            V1JSONSchemaProps result = new V1JSONSchemaProps();

            if (propertyType.Equals(typeof(string)))
            {
                result.Type = "string";
            }
            else if (propertyType.Equals(typeof(int)))
            {
                result.Type = "integer";
            }
            else if (propertyType.Equals(typeof(long)))
            {
                result.Type = "integer";
            }
            else if (propertyType.Equals(typeof(object)))
            {
                result.Type = "object";
                result.XKubernetesPreserveUnknownFields = true;
            }
            else if (propertyType.IsEnum)
            {
                result.Type = "string";
                result.EnumProperty = new List<object>();
                foreach (string? enumName in propertyType.GetEnumNames())
                {
                    result.EnumProperty.Add(enumName);
                }
            }
            else if (propertyType.IsArray)
            {
                // Array types are not yet implemented.
                throw new NotImplementedException();
            }
            else if (propertyType.IsGenericType)
            {
                Type genericTypeDefinition = propertyType.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(Dictionary<,>))
                {
                    result.Type = "object";
                    var additionalProperties = new V1JSONSchemaProps();

                    if (propertyType.GetGenericArguments()[0] != typeof(string))
                        throw new NotImplementedException();

                    if (propertyType.GetGenericArguments()[1] == typeof(string))
                    {
                        additionalProperties.Type = "string";
                    }
                    else if (propertyType.GetGenericArguments()[1] == typeof(bool))
                    {
                        additionalProperties.Type = "boolean";
                    }
                    else if (propertyType.GetGenericArguments()[1] == typeof(int))
                    {
                        additionalProperties.Type = "integer";
                    }
                    else if (propertyType.GetGenericArguments()[1] == typeof(long))
                    {
                        additionalProperties.Type = "integer";
                    }
                    else
                    {
                        additionalProperties.Type = "object";
                        try
                        {
                            additionalProperties.Properties = ProcessProperties(propertyType.GetGenericArguments()[1]);
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"Error processing dictionary type {propertyType.Name}", ex);
                        }
                    }
                    result.AdditionalProperties = additionalProperties;
                }
                else if (genericTypeDefinition == typeof(List<>))
                {
                    result.Type = "array";
                    var items = new V1JSONSchemaProps();

                    var listType = propertyType.GetGenericArguments()[0]; 
                    if (listType == typeof(string))
                    {
                        items.Type = "string";
                    }
                    else
                    {
                        items.Type = "object";
                        try
                        {
                            items.Properties = ProcessProperties(listType);
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"Error processing list type {listType.Name}", ex);
                        }
                    }
                    result.Items = items;
                }
                else if (genericTypeDefinition == typeof(Nullable<>))
                {
                    try 
                    {
                        return ProcessProperty(propertyType.GetGenericArguments()[0]);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Error processing nullable type {propertyType.Name}", ex);
                    }
                }
                else if(genericTypeDefinition == typeof(System.Collections.Generic.IEqualityComparer<>))
                {
                    // do nothing
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
            else
            {
                // For complex types, recursively process their properties.
                result.Type = "object";
                result.Properties = ProcessProperties(propertyType);
            }
            return result;
        }

        /// <summary>
        /// Creates a custom resource definition (CRD) for a given type.
        /// </summary>
        /// <typeparam name="T">The type for which the CRD is to be created.</typeparam>
        /// <returns>The custom resource definition for the given type.</returns>
        public V1CustomResourceDefinition Create<T>()
        {
            Type type = typeof(T);
            var attribute = (KubernetesCustomResourceAttribute)type.GetCustomAttributes(typeof(KubernetesCustomResourceAttribute), false).First();

            var names = new V1CustomResourceDefinitionNames()
            {
                Kind = attribute.Kind,
                Plural = attribute.PluralName.ToLower(),
                Singular = attribute.Kind.ToLower(),
            };

            var spec = new V1CustomResourceDefinitionSpec()
            {
                Scope = "Namespaced",
                Group = attribute.Group,
                Names = names
            };

            var categories = type.GetCustomAttributes(typeof(KubernetesCategoryNameAttribute), false);
            if (categories.Length > 0)
            {
                var categoryNames = categories.Select(c => ((KubernetesCategoryNameAttribute)c).Name).ToList();
                spec.Names.Categories = categoryNames;
            }

            var shortNames = type.GetCustomAttributes(typeof(KubernetesShortNameAttribute), false);
            if (shortNames.Length > 0)
            {
                var shortNameList = shortNames.Select(c => ((KubernetesShortNameAttribute)c).Name).ToList();
                spec.Names.ShortNames = shortNameList;
            }

            
            V1CustomResourceSubresources? subresources = null;
            if(attribute.HasStatusField)
            {
                subresources = new V1CustomResourceSubresources()
                {
                    Status = new object()
                };
            }

            spec.Versions = new List<V1CustomResourceDefinitionVersion>(){
                new V1CustomResourceDefinitionVersion(){
                    Name = attribute.Version,
                    Served = true,
                    Storage = true,
                    Schema = new V1CustomResourceValidation(){
                        OpenAPIV3Schema = new V1JSONSchemaProps(){
                            Type = "object",
                            Properties = ProcessProperties(type)
                        }
                    },
                    Subresources = subresources
                }
            };



            return new V1CustomResourceDefinition()
            {
                Spec = spec,
                Metadata = new V1ObjectMeta()
                {
                    Name = $"{attribute.PluralName.ToLower()}.{attribute.Group}"
                }
            };
        }
    }
}